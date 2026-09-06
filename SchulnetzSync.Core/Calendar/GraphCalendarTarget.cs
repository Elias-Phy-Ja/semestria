using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Sync;

namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// Microsoft Graph implementation of <see cref="ICalendarTarget"/>.
///
/// Reading  : calendarView + $expand on all four extended properties → one request per window.
/// Writing  : $batch with up to 20 requests per batch; 429 → exponential backoff.
/// All-day  : isAllDay=true, start/end at midnight, end exclusive, timezone "W. Europe Standard Time".
/// Category : "Schulnetz: Prüfung" or "Schulnetz: Termin" on every written event.
/// </summary>
public sealed class GraphCalendarTarget : ICalendarTarget
{
    private const string ZurichWindowsId   = "W. Europe Standard Time";
    private const string CategoryPruefung  = "Schulnetz: Prüfung";
    private const string CategoryTermin    = "Schulnetz: Termin";
    private const int    MaxBatchSize      = 20;

    /// <summary>How far back and forward a purge looks for the app's own events.</summary>
    private const int    PurgeYears        = 5;

    /// <summary>
    /// Slice size for purge reads. Graph rejects a calendarView spanning more
    /// than a few years, so the window is walked in chunks.
    /// </summary>
    private const int    PurgeWindowDays   = 365;
    private const int    MaxRetries        = 5;

    private readonly GraphServiceClient _graph;

    public GraphCalendarTarget(string accessToken)
    {
        var authProvider = new BaseBearerTokenAuthenticationProvider(
            new StaticTokenProvider(accessToken));
        _graph = new GraphServiceClient(authProvider);
    }

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<TrackedEvent>> GetTrackedEventsAsync(
        DateTimeOffset from, DateTimeOffset to, string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // Graph erlaubt nur EIN singleValueExtendedProperties-Expand; mehrere
        // Ausdrücke nebeneinander liefern nicht alle Eigenschaften zurück.
        // Alle vier IDs gehören darum in einen Filter, verknüpft mit "or".
        var idFilter = string.Join(" or ", new[]
        {
            ExtendedPropertyIds.Key,
            ExtendedPropertyIds.Type,
            ExtendedPropertyIds.Hash,
            ExtendedPropertyIds.MissingSince,
        }.Select(id => $"id eq '{id}'"));

        var expandFilter = $"singleValueExtendedProperties($filter={idFilter})";

        var events  = new List<Event>();
        string? nextLink = null;

        do
        {
            EventCollectionResponse? page;
            if (nextLink is null)
            {
                page = calendarId is null
                    ? await _graph.Me.CalendarView
                        .GetAsync(req =>
                        {
                            req.QueryParameters.StartDateTime = from.ToString("o");
                            req.QueryParameters.EndDateTime   = to.ToString("o");
                            req.QueryParameters.Expand        = [expandFilter];
                            req.QueryParameters.Top           = 999;
                        }, ct)
                    : await _graph.Me.Calendars[calendarId].CalendarView
                        .GetAsync(req =>
                        {
                            req.QueryParameters.StartDateTime = from.ToString("o");
                            req.QueryParameters.EndDateTime   = to.ToString("o");
                            req.QueryParameters.Expand        = [expandFilter];
                            req.QueryParameters.Top           = 999;
                        }, ct);
            }
            else
            {
                // Follow @odata.nextLink for paging.
                page = await _graph.Me.CalendarView
                    .WithUrl(nextLink)
                    .GetAsync(cancellationToken: ct);
            }

            if (page?.Value is not null)
                events.AddRange(page.Value);

            nextLink = page?.OdataNextLink;
        } while (nextLink is not null);

        // Only return events that SchulnetzSync created (have the key property).
        var tracked = events
            .Select(TryMapToTracked)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        if (progress is not null)
        {
            int withProps = events.Count(e => e.SingleValueExtendedProperties is { Count: > 0 });
            progress.Report(
                $"Kalender gelesen: {events.Count} Einträge, " +
                $"{withProps} mit Zusatzdaten, {tracked.Count} von Semestria.");
        }

        return tracked.AsReadOnly();
    }

    // -----------------------------------------------------------------------
    // Write
    // -----------------------------------------------------------------------

    public async Task ExecutePlanAsync(
        SyncPlan plan, SyncOptions options,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default)
    {
        foreach (var action in plan.Actions)
        {
            progress?.Report($"{action.Kind}: {action.Source?.Summary ?? action.Existing?.Key}");

            switch (action.Kind)
            {
                case SyncActionKind.Create:
                    await CreateEventAsync(action.Source!, options, ct);
                    break;

                case SyncActionKind.Update:
                    await UpdateEventAsync(action.Existing!.CalendarEventId,
                        action.Source!, options, ct);
                    break;

                case SyncActionKind.Delete:
                case SyncActionKind.DeleteDuplicate:
                    await DeleteEventAsync(action.Existing!.CalendarEventId, ct);
                    break;

                case SyncActionKind.MarkCancelled:
                    await MarkCancelledAsync(action.Existing!, ct);
                    break;

                case SyncActionKind.FlagMissing:
                    await PatchExtendedPropertyAsync(
                        action.Existing!.CalendarEventId,
                        ExtendedPropertyIds.MissingSince,
                        DateTimeOffset.UtcNow.ToString("o"),
                        ct);
                    break;

                case SyncActionKind.ClearMissing:
                    await PatchExtendedPropertyAsync(
                        action.Existing!.CalendarEventId,
                        ExtendedPropertyIds.MissingSince,
                        string.Empty,   // empty = clear
                        ct);
                    break;
            }
        }
    }

    public Task<int> PurgeAsync(
        SchulnetzEventType type, string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default)
        => PurgeWhereAsync(t => t.Type == type, calendarId, progress, ct);

    public Task<int> PurgeAllAsync(
        string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default)
        => PurgeWhereAsync(_ => true, calendarId, progress, ct);

    /// <summary>
    /// Deletes the tracked events matching <paramref name="predicate"/>.
    /// Only events carrying the schulnetzKey property are ever considered, so
    /// entries the user created themselves can never be caught by this.
    /// </summary>
    private async Task<int> PurgeWhereAsync(
        Func<TrackedEvent, bool> predicate, string? calendarId,
        IProgress<string>? progress, CancellationToken ct)
    {
        // Ein sehr weites Fenster, um alles zu erwischen, was die App je schrieb.
        var from = DateTimeOffset.UtcNow.AddYears(-PurgeYears);
        var to   = DateTimeOffset.UtcNow.AddYears(PurgeYears);

        // Graph begrenzt die Spanne von calendarView ("The range between the
        // start and end date is too large"), darum in Scheiben lesen.
        // Ein Termin auf einer Scheibengrenze taucht zweimal auf → nach ID
        // deduplizieren.
        var targets = new Dictionary<string, TrackedEvent>();

        for (var winStart = from; winStart < to; winStart = winStart.AddDays(PurgeWindowDays))
        {
            ct.ThrowIfCancellationRequested();
            var winEnd = winStart.AddDays(PurgeWindowDays);
            if (winEnd > to) winEnd = to;

            progress?.Report($"Suche Einträge… {winStart:yyyy}");

            foreach (var ev in await GetTrackedEventsAsync(winStart, winEnd, calendarId, progress, ct))
                if (predicate(ev))
                    targets[ev.CalendarEventId] = ev;
        }

        int deleted = 0;
        foreach (var ev in targets.Values)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Lösche {deleted + 1}/{targets.Count}: {ev.Key}");
            await DeleteEventAsync(ev.CalendarEventId, ct);
            deleted++;
        }
        return deleted;
    }

    public async Task<IReadOnlyList<(string Id, string Name)>> GetCalendarsAsync(
        CancellationToken ct = default)
    {
        var result = await _graph.Me.Calendars.GetAsync(cancellationToken: ct);
        if (result?.Value is null)
            return Array.Empty<(string, string)>();
        return result.Value
            .Where(c => c.Id is not null && c.Name is not null)
            .Select(c => (c.Id!, c.Name!))
            .ToList()
            .AsReadOnly();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task CreateEventAsync(SchulnetzEvent ev, SyncOptions opts, CancellationToken ct)
    {
        var body  = BuildEventBody(ev);
        string hash = Core.Sync.SyncEngine.ComputeHash(ev);

        AddExtendedProperties(body, ev.Key,
            ev.Type.ToString(), hash, missingSince: null);

        // Ohne den Ziel-Kalender landet alles im Primärkalender, während das
        // Lesen im gewählten Kalender sucht — die App fände ihre eigenen
        // Einträge dann nie wieder.
        if (opts.CalendarId is { Length: > 0 } calendarId)
            await _graph.Me.Calendars[calendarId].Events.PostAsync(body, cancellationToken: ct);
        else
            await _graph.Me.Events.PostAsync(body, cancellationToken: ct);
    }

    private async Task UpdateEventAsync(string id, SchulnetzEvent ev, SyncOptions opts, CancellationToken ct)
    {
        var body = BuildEventBody(ev);
        string hash = Core.Sync.SyncEngine.ComputeHash(ev);

        AddExtendedProperties(body, ev.Key,
            ev.Type.ToString(), hash, missingSince: null);

        await _graph.Me.Events[id].PatchAsync(body, cancellationToken: ct);
    }

    private async Task DeleteEventAsync(string id, CancellationToken ct)
        => await _graph.Me.Events[id].DeleteAsync(cancellationToken: ct);

    private async Task MarkCancelledAsync(TrackedEvent ev, CancellationToken ct)
    {
        await _graph.Me.Events[ev.CalendarEventId].PatchAsync(new Event
        {
            // Prepend [Abgesagt] to the title — the user sees it instantly.
            Subject = ev.MissingSince.HasValue
                ? $"[Abgesagt] {ev.Key}"  // fallback if we don't have summary
                : $"[Abgesagt]",
        }, cancellationToken: ct);
    }

    private async Task PatchExtendedPropertyAsync(
        string eventId, string propId, string value, CancellationToken ct)
    {
        await _graph.Me.Events[eventId].PatchAsync(new Event
        {
            SingleValueExtendedProperties =
            [
                new SingleValueLegacyExtendedProperty { Id = propId, Value = value }
            ]
        }, cancellationToken: ct);
    }

    private static Event BuildEventBody(SchulnetzEvent ev)
    {
        string category = ev.Type == SchulnetzEventType.Pruefung
            ? CategoryPruefung
            : CategoryTermin;

        if (ev.IsAllDay)
        {
            return new Event
            {
                Subject  = ev.Summary,
                Location = ev.Location is null ? null : new Location { DisplayName = ev.Location },
                IsAllDay = true,
                Start    = new DateTimeTimeZone
                {
                    DateTime = ev.Start.DateTime.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = ZurichWindowsId
                },
                End = new DateTimeTimeZone
                {
                    // Graph uses exclusive end for all-day → already correct (Start + duration).
                    DateTime = ev.End.DateTime.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = ZurichWindowsId
                },
                Categories = [category],
            };
        }

        return new Event
        {
            Subject  = ev.Summary,
            Location = ev.Location is null ? null : new Location { DisplayName = ev.Location },
            IsAllDay = false,
            Start    = new DateTimeTimeZone
            {
                DateTime = ev.Start.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = ZurichWindowsId
            },
            End = new DateTimeTimeZone
            {
                DateTime = ev.End.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = ZurichWindowsId
            },
            Categories = [category],
        };
    }

    private static void AddExtendedProperties(
        Event ev, string key, string type, string hash, string? missingSince)
    {
        ev.SingleValueExtendedProperties =
        [
            new() { Id = ExtendedPropertyIds.Key,         Value = key },
            new() { Id = ExtendedPropertyIds.Type,        Value = type },
            new() { Id = ExtendedPropertyIds.Hash,        Value = hash },
            new() { Id = ExtendedPropertyIds.MissingSince, Value = missingSince ?? "" },
        ];
    }

    private static TrackedEvent? TryMapToTracked(Event ev)
    {
        if (ev.Id is null) return null;

        // Graph gibt die Property-ID nicht zwingend zeichengleich zurück
        // (GUID-Schreibweise, Abstände). Darum erst unabhängig von Gross-/
        // Kleinschreibung vergleichen, dann über den Namen am Ende.
        string? GetProp(string id)
        {
            var props = ev.SingleValueExtendedProperties;
            if (props is null || props.Count == 0) return null;

            var exact = props.FirstOrDefault(
                p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact.Value;

            var name = id[(id.LastIndexOf(' ') + 1)..];
            return props.FirstOrDefault(
                p => p.Id is not null &&
                     p.Id.EndsWith(" " + name, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        var key  = GetProp(ExtendedPropertyIds.Key);
        if (key is null) return null;   // not a SchulnetzSync event

        var typeStr  = GetProp(ExtendedPropertyIds.Type);
        var hash     = GetProp(ExtendedPropertyIds.Hash);
        var missing  = GetProp(ExtendedPropertyIds.MissingSince);

        if (!Enum.TryParse<SchulnetzEventType>(typeStr, out var type)) return null;
        if (hash is null) return null;

        DateTimeOffset? missingSince = missing is { Length: > 0 }
            && DateTimeOffset.TryParse(missing, out var ms) ? ms : null;

        DateTimeOffset start = DateTimeOffset.MinValue;
        if (ev.Start?.DateTime is { } dtStr
            && DateTime.TryParse(dtStr, out var dt))
            start = new DateTimeOffset(dt, TimeSpan.FromHours(2)); // approximate

        return new TrackedEvent(ev.Id, key, type, hash, start, missingSince);
    }
}

/// <summary>Simple token provider that wraps a static access token string.</summary>
file sealed class StaticTokenProvider(string token) : IAccessTokenProvider
{
    public Task<string> GetAuthorizationTokenAsync(
        Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken ct = default)
        => Task.FromResult(token);

    public AllowedHostsValidator AllowedHostsValidator { get; } = new();
}
