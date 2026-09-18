using System.Security.Cryptography;
using System.Text;
using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Sync;

/// <summary>
/// The diff: feed events plus calendar state in, action plan out. A pure function —
/// no network, no clock, no files — which is what makes every rule below testable.
/// </summary>
public static class SyncEngine
{
    // Safety limits for mass deletions. Both have to be exceeded before a run is blocked,
    // so a small calendar losing two entries does not trip anything.
    private const double MaxDeleteFraction = 0.20;
    private const int MaxDeleteAbsolute    = 5;

    // How long an event may be absent from the feed before we act on it.
    private const int MissingGracePeriodH  = 24;

    /// <summary>Works out what has to happen to bring the calendar in line with the feed.</summary>
    /// <param name="feedEvents">Everything parsed from the feed, all types.</param>
    /// <param name="tracked">What we wrote earlier, as read back from Graph.</param>
    /// <param name="options">Settings for this run.</param>
    /// <param name="feedHealth">Result of the plausibility check on the raw feed.</param>
    /// <param name="now">Current time, injected so the tests stay deterministic.</param>
    public static SyncPlan Build(
        IReadOnlyList<SchulnetzEvent> feedEvents,
        IReadOnlyList<TrackedEvent>   tracked,
        SyncOptions                   options,
        FeedHealth                    feedHealth,
        DateTimeOffset                now)
    {
        // Step 1 — fill in missing exam rooms first, so the room is part of the hash.
        var effective = options.EnrichExamLocationFromLesson
            ? EnrichExamLocations(feedEvents)
            : feedEvents;

        // How far the feed reaches. Anything outside that span is none of our business.
        DateTimeOffset? feedMin = effective.Count > 0 ? effective.Min(e => e.Start) : null;
        DateTimeOffset? feedMax = effective.Count > 0 ? effective.Max(e => e.Start) : null;

        // Hand-made entries come from the local list, not from the feed, so they are always
        // synced. The type switches decide how much of the feed we take over, not what the
        // user created themselves.
        bool IsSyncable(SchulnetzEventType type, string key)
            => EventKeys.IsManual(key) || options.EnabledTypes.Contains(type);

        var feedByKey    = effective.Where(e => IsSyncable(e.Type, e.Key))
                                    .ToDictionary(e => e.Key);

        // The same key can sit in the calendar twice when an earlier run failed to
        // recognise its own entries and created them again. First one wins, the rest go.
        var byKey        = tracked.GroupBy(t => t.Key).ToList();
        var canonical    = byKey.Select(g => g.First()).ToList();
        var trackedByKey = canonical.ToDictionary(t => t.Key);

        var actions  = new List<SyncAction>();
        var blockers = new List<string>();

        foreach (var group in byKey)
            foreach (var surplus in group.Skip(1))
                actions.Add(new SyncAction(SyncActionKind.DeleteDuplicate, null, surplus,
                    "Doppelter Eintrag mit gleichem Schlüssel"));
        // ── Step 2 — feed to calendar: create, update, clear the missing flag ──
        foreach (var ev in effective)
        {
            // Lektionen and switched-off types never get this far.
            if (!IsSyncable(ev.Type, ev.Key))
                continue;

            string hash = ComputeHash(ev);

            if (!trackedByKey.TryGetValue(ev.Key, out var existing))
            {
                // Rule 3 — key we have never seen.
                actions.Add(new SyncAction(SyncActionKind.Create, ev, null,
                    "Neuer Termin im Feed"));
            }
            else
            {
                if (existing.Hash != hash)
                {
                    // Rule 4a — known key, content moved.
                    actions.Add(new SyncAction(SyncActionKind.Update, ev, existing,
                        "Inhalt hat sich geändert"));
                }
                else if (existing.MissingSince.HasValue)
                {
                    // Rule 4b — unchanged, and it is back after having been flagged.
                    actions.Add(new SyncAction(SyncActionKind.ClearMissing, ev, existing,
                        "Termin ist wieder im Feed aufgetaucht"));
                }
                // Rule 4c — unchanged and never flagged: nothing to do.
            }
        }

        // ── Step 3 — calendar to feed: flag, delete, mark cancelled ──
        foreach (var tr in canonical)
        {
            if (!IsSyncable(tr.Type, tr.Key))
                continue;

            // Still in the feed, so step 2 already dealt with it.
            if (feedByKey.ContainsKey(tr.Key))
                continue;

            // Rule 5e — for hand-made entries the local list is the only source. Gone from
            // there means the user deleted it. The safeguards below exist for a broken feed
            // and make no sense here, so remove it right away.
            if (EventKeys.IsManual(tr.Key))
            {
                actions.Add(new SyncAction(SyncActionKind.Delete, null, tr,
                    "Manueller Eintrag wurde in der App gelöscht"));
                continue;
            }

            // Rule 5a — outside the window the feed covers, so its absence means nothing.
            if (feedMin.HasValue && feedMax.HasValue)
            {
                if (tr.Start < feedMin.Value || tr.Start > feedMax.Value)
                    continue;
            }

            // Rule 5b — never touch events that already happened.
            if (tr.Start < now)
                continue;

            if (!tr.MissingSince.HasValue)
            {
                // Rule 5c — first time it is gone. Only make a note of it; feeds hiccup.
                actions.Add(new SyncAction(SyncActionKind.FlagMissing, null, tr,
                    "Nicht mehr im Feed — warte auf nächsten Lauf"));
            }
            else if ((now - tr.MissingSince.Value).TotalHours >= MissingGracePeriodH)
            {
                // Rule 5d — gone long enough to believe it. Exams get retitled instead of
                // deleted, so a cancelled exam is still visible in the calendar.
                bool cancel = options.CancelInsteadOfDelete
                           && tr.Type == SchulnetzEventType.Pruefung;

                actions.Add(cancel
                    ? new SyncAction(SyncActionKind.MarkCancelled, null, tr,
                        $"Seit {MissingGracePeriodH}h nicht im Feed — als Abgesagt markiert")
                    : new SyncAction(SyncActionKind.Delete, null, tr,
                        $"Seit {MissingGracePeriodH}h nicht im Feed — wird gelöscht"));
            }
            // Otherwise it is missing but still inside the grace period: wait.
        }
        // ── Step 4 — blockers. Actions stay in the plan so the dry-run can show
        //    what would have happened, but CanExecute turns false.

        // A: the feed itself looks wrong.
        if (!feedHealth.IsHealthy)
            foreach (var p in feedHealth.Problems)
                blockers.Add($"Feed-Problem: {p}");

        // B: we track entries of a type the feed suddenly has none of. Much more likely
        // a broken feed than a school cancelling every exam at once.
        foreach (var type in options.EnabledTypes)
        {
            int trackedCount = canonical.Count(t => t.Type == type && !EventKeys.IsManual(t.Key));
            int feedCount    = effective.Count(e => e.Type == type && !EventKeys.IsManual(e.Key));
            if (trackedCount > 0 && feedCount == 0)
                blockers.Add(
                    $"Kalender hat {trackedCount} {type}-Einträge, Feed enthält aber keine — " +
                    "möglicher Feed-Fehler.");
        }

        // C: too many deletions in one run. Needs both limits to be crossed, so a handful
        // of genuinely cancelled exams still goes through.
        foreach (var type in options.EnabledTypes)
        {
            int deletions = actions.Count(a =>
                a.Kind is SyncActionKind.Delete or SyncActionKind.MarkCancelled
                && a.Existing?.Type == type
                && !EventKeys.IsManual(a.Existing.Key));

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

    /// <summary>
    /// SHA-256 over the fields whose change is worth a calendar write. "o" gives an
    /// unambiguous, sortable timestamp, so the hash does not wobble with the locale.
    /// </summary>
    public static string ComputeHash(SchulnetzEvent ev)
    {
        string input =
            $"{ev.Start:o}|{ev.End:o}|{ev.Summary}|{ev.Location ?? ""}|{ev.IsAllDay}";

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Exams often arrive without a room. The lesson that starts at the same moment
    /// usually has it, so borrow it from there.
    /// </summary>
    private static IReadOnlyList<SchulnetzEvent> EnrichExamLocations(
        IReadOnlyList<SchulnetzEvent> events)
    {
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
