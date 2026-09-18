using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Sync;

namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// The Outlook calendar as far as the rest of the app is concerned.
/// <see cref="GraphCalendarTarget"/> in production, in-memory fakes in the tests —
/// which is what lets the whole sync run without a Microsoft account.
/// </summary>
public interface ICalendarTarget
{
    /// <summary>
    /// All events we manage inside the time window, extended properties included.
    /// One Graph request per window (calendarView + $expand).
    /// </summary>
    /// <param name="progress">
    /// Gets a diagnostic line with how many events were read and how many carry our
    /// marker. That distinction matters: it separates "calendar is empty" from
    /// "markers were not recognised", which look identical from the outside.
    /// </param>
    Task<IReadOnlyList<TrackedEvent>> GetTrackedEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Runs the plan through Graph $batch, 20 requests at a time, honouring
    /// Retry-After on 429.
    /// </summary>
    Task ExecutePlanAsync(
        SyncPlan     plan,
        SyncOptions  options,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default);

    /// <summary>
    /// Deletes every event with schulnetzType == <paramref name="type"/>, behind the
    /// "Remove all" action in the settings.
    /// </summary>
    /// <returns>How many events were deleted.</returns>
    Task<int> PurgeAsync(
        SchulnetzEventType type,
        string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default);

    /// <summary>
    /// Deletes everything this app ever created, whatever the type, cancelled entries
    /// included. Only events carrying schulnetzKey are touched, so anything the user
    /// wrote themselves stays where it is.
    /// </summary>
    /// <returns>How many events were deleted.</returns>
    Task<int> PurgeAllAsync(
        string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default);

    /// <summary>Calendars the signed-in account can write to, for the picker in the settings.</summary>
    Task<IReadOnlyList<(string Id, string Name)>> GetCalendarsAsync(
        CancellationToken ct = default);
}
