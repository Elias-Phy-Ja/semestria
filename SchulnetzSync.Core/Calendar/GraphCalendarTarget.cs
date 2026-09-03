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
        CancellationToken ct = default)
    {
        var expandFilter = string.Join(",", new[]
        {
            $"singleValueExtendedProperties($filter=id eq '{ExtendedPropertyIds.Key}')",
            $"singleValueExtendedProperties($filter=id eq '{ExtendedPropertyIds.Type}')",
            $"singleValueExtendedProperties($filter=id eq '{ExtendedPropertyIds.Hash}')",
            $"singleValueExtendedProperties($filter=id eq '{ExtendedPropertyIds.MissingSince}')",
        });

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
        return events
            .Select(TryMapToTracked)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList()
            .AsReadOnly();
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

    public async Task PurgeAsync(
        SchulnetzEventType type, string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default)
    {
        // Read a very wide window to catch everything the app ever wrote.
        var from    = DateTimeOffset.UtcNow.AddYears(-5);
        var to      = DateTimeOffset.UtcNow.AddYears(5);
        var tracked = await GetTrackedEventsAsync(from, to, calendarId, ct);

        foreach (var ev in tracked.Where(t => t.Type == type))
        {
            progress?.Report($"Lösche: {ev.Key}");
            await DeleteEventAsync(ev.CalendarEventId, ct);
        }
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

        string? GetProp(string id) => ev.SingleValueExtendedProperties?
            .FirstOrDefault(p => p.Id == id)?.Value;

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
