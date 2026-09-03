using System.Security.Cryptography;
using System.Text;
using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Sync;

/// <summary>
/// Pure diff function: feed events + calendar state → action plan.
/// No network, no clock, no files — everything comes in as parameters.
/// </summary>
public static class SyncEngine
{
    private const double MaxDeleteFraction = 0.20;
    private const int MaxDeleteAbsolute    = 5;
    private const int MissingGracePeriodH  = 24;

    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes the set of actions needed to bring the calendar in sync with the feed.
    /// </summary>
    /// <param name="feedEvents">All events parsed from the iCal feed (all types).</param>
    /// <param name="tracked">Events previously written by SchulnetzSync, read from Graph.</param>
    /// <param name="options">Sync configuration for this run.</param>
    /// <param name="feedHealth">Result of the feed plausibility check.</param>
    /// <param name="now">Current wall-clock time, injected for deterministic testing.</param>
    public static SyncPlan Build(
        IReadOnlyList<SchulnetzEvent> feedEvents,
        IReadOnlyList<TrackedEvent>   tracked,
        SyncOptions                   options,
        FeedHealth                    feedHealth,
        DateTimeOffset                now)
    {
        // Step 1 — Optionally enrich exam locations from concurrent lessons.
        // This happens before hash computation so the room ends up in the hash.
        var effective = options.EnrichExamLocationFromLesson
            ? EnrichExamLocations(feedEvents)
            : feedEvents;

        // Compute the temporal span of the feed (used to ignore events outside the window).
        DateTimeOffset? feedMin = effective.Count > 0 ? effective.Min(e => e.Start) : null;
        DateTimeOffset? feedMax = effective.Count > 0 ? effective.Max(e => e.Start) : null;

        // Build fast-lookup maps keyed by the stable Schulnetz key.
        var feedByKey    = effective.Where(e => options.EnabledTypes.Contains(e.Type))
                                    .ToDictionary(e => e.Key);
        var trackedByKey = tracked.ToDictionary(t => t.Key);

        var actions  = new List<SyncAction>();
        var blockers = new List<string>();

        // ----------------------------------------------------------------
        // Step 2 — Feed → Calendar: Create / Update / ClearMissing
        // ----------------------------------------------------------------
        foreach (var ev in effective)
        {
            // Lektionen and disabled types are completely ignored.
            if (!options.EnabledTypes.Contains(ev.Type))
                continue;

            string hash = ComputeHash(ev);

            if (!trackedByKey.TryGetValue(ev.Key, out var existing))
            {
                // Rule 3: new key → create.
                actions.Add(new SyncAction(SyncActionKind.Create, ev, null,
                    "Neuer Termin im Feed"));
            }
            else
            {
                if (existing.Hash != hash)
                {
                    // Rule 4a: same key, different content → update.
                    actions.Add(new SyncAction(SyncActionKind.Update, ev, existing,
                        "Inhalt hat sich geändert"));
                }
                else if (existing.MissingSince.HasValue)
                {
                    // Rule 4b: same key, same hash, was flagged missing → clear flag.
                    actions.Add(new SyncAction(SyncActionKind.ClearMissing, ev, existing,
                        "Termin ist wieder im Feed aufgetaucht"));
                }
                // Rule 4c: same key, same hash, no MissingSince → nothing to do.
            }
        }

        // ----------------------------------------------------------------
        // Step 3 — Calendar → Feed: FlagMissing / Delete / MarkCancelled
        // ----------------------------------------------------------------
        foreach (var tr in tracked)
        {
            // Only process enabled types.
            if (!options.EnabledTypes.Contains(tr.Type))
                continue;

            // Already handled above (event still in feed).
            if (feedByKey.ContainsKey(tr.Key))
                continue;

            // Rule 5a — outside the feed window → ignore (feed may not cover old events).
            if (feedMin.HasValue && feedMax.HasValue)
            {
                if (tr.Start < feedMin.Value || tr.Start > feedMax.Value)
                    continue;
            }

            // Rule 5b — already happened → don't delete past events.
            if (tr.Start < now)
                continue;

            if (!tr.MissingSince.HasValue)
            {
                // Rule 5c — first absence → stamp MissingSince, don't delete yet.
                actions.Add(new SyncAction(SyncActionKind.FlagMissing, null, tr,
                    "Nicht mehr im Feed — warte auf nächsten Lauf"));
            }
            else if ((now - tr.MissingSince.Value).TotalHours >= MissingGracePeriodH)
            {
                // Rule 5d — missing for 24 h+ → delete or mark cancelled.
                bool cancel = options.CancelInsteadOfDelete
                           && tr.Type == SchulnetzEventType.Pruefung;

                actions.Add(cancel
                    ? new SyncAction(SyncActionKind.MarkCancelled, null, tr,
                        $"Seit {MissingGracePeriodH}h nicht im Feed — als Abgesagt markiert")
                    : new SyncAction(SyncActionKind.Delete, null, tr,
                        $"Seit {MissingGracePeriodH}h nicht im Feed — wird gelöscht"));
            }
            // Else: missing but grace period not expired → wait.
        }

        // ----------------------------------------------------------------
        // Step 4 — Blockers (actions are kept for display even when blocked)
        // ----------------------------------------------------------------

        // Blocker A: unhealthy feed.
        if (!feedHealth.IsHealthy)
            foreach (var p in feedHealth.Problems)
                blockers.Add($"Feed-Problem: {p}");

        // Blocker B: an enabled type has tracked entries but zero feed entries.
        foreach (var type in options.EnabledTypes)
        {
            int trackedCount = tracked.Count(t => t.Type == type);
            int feedCount    = effective.Count(e => e.Type == type);
            if (trackedCount > 0 && feedCount == 0)
                blockers.Add(
                    $"Kalender hat {trackedCount} {type}-Einträge, Feed enthält aber keine — " +
                    "möglicher Feed-Fehler.");
        }

        // Blocker C: too many deletes at once.
        foreach (var type in options.EnabledTypes)
        {
            int deletions = actions.Count(a =>
                a.Kind is SyncActionKind.Delete or SyncActionKind.MarkCancelled
                && a.Existing?.Type == type);

            int managed = tracked.Count(t => t.Type == type);

            if (managed > 0
                && deletions > MaxDeleteAbsolute
                && (double)deletions / managed > MaxDeleteFraction)
            {
                blockers.Add(
                    $"Würde {deletions}/{managed} {type}-Einträge löschen " +
                    $"({deletions * 100 / managed}% > {MaxDeleteFraction * 100:0}%) — " +
                    "Sicherheitsstopp.");
            }
        }

        return new SyncPlan(actions.AsReadOnly(), blockers.AsReadOnly());
    }

    // -----------------------------------------------------------------------
    // Hash
    // -----------------------------------------------------------------------

    /// <summary>
    /// SHA-256 over the fields that, when changed, warrant a calendar update.
    /// The "o" format produces a sortable, unambiguous DateTimeOffset string.
    /// </summary>
    public static string ComputeHash(SchulnetzEvent ev)
    {
        string input =
            $"{ev.Start:o}|{ev.End:o}|{ev.Summary}|{ev.Location ?? ""}|{ev.IsAllDay}";

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<SchulnetzEvent> EnrichExamLocations(
        IReadOnlyList<SchulnetzEvent> events)
    {
        // Build a map: start time → first lesson room at that time.
        var lessonRooms = events
            .Where(e => e.Type == SchulnetzEventType.Lektion && e.Location is not null)
            .GroupBy(e => e.Start)
            .ToDictionary(g => g.Key, g => g.First().Location!);

        var result = new List<SchulnetzEvent>(events.Count);
        foreach (var ev in events)
        {
            if (ev.Type == SchulnetzEventType.Pruefung
                && ev.Location is null
                && lessonRooms.TryGetValue(ev.Start, out var room))
            {
                result.Add(ev with { Location = room });
            }
            else
            {
                result.Add(ev);
            }
        }
        return result.AsReadOnly();
    }
}
