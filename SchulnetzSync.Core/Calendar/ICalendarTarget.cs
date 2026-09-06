using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Sync;

namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// Abstraction over the Outlook calendar (Microsoft Graph).
/// Implemented by <see cref="GraphCalendarTarget"/> in production
/// and by in-memory fakes in tests.
/// </summary>
public interface ICalendarTarget
{
    /// <summary>
    /// Returns all SchulnetzSync-managed events in the given time window,
    /// including their extended properties.
    /// One Graph request per window (calendarView + $expand).
    /// </summary>
    /// <param name="progress">
    /// Receives a diagnostic line stating how many events were read and how many
    /// of them carry this app's marker. Useful to tell "calendar is empty" apart
    /// from "markers were not recognised".
    /// </param>
    Task<IReadOnlyList<TrackedEvent>> GetTrackedEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes the sync plan (Create / Update / Delete / Flag / Clear / Cancel)
    /// using Graph $batch (max 20 per batch). Respects 429 Retry-After.
    /// </summary>
    Task ExecutePlanAsync(
        SyncPlan     plan,
        SyncOptions  options,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default);

    /// <summary>
    /// Deletes every calendar event that carries schulnetzType == <paramref name="type"/>.
    /// Used by the "Remove all" action in the UI.
    /// </summary>
    /// <returns>The number of events deleted.</returns>
    Task<int> PurgeAsync(
        SchulnetzEventType type,
        string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default);

    /// <summary>
    /// Deletes every event this app ever created, regardless of type — including
    /// entries already marked as cancelled. Events the user created themselves
    /// are untouched: only events carrying the schulnetzKey property are removed.
    /// </summary>
    /// <returns>The number of events deleted.</returns>
    Task<int> PurgeAllAsync(
        string? calendarId,
        IProgress<string>? progress = null,
        CancellationToken  ct       = default);

    /// <summary>
    /// Returns the list of calendars available to the signed-in account.
    /// Used to populate the calendar selection combo box in the UI.
    /// </summary>
    Task<IReadOnlyList<(string Id, string Name)>> GetCalendarsAsync(
        CancellationToken ct = default);
}
