namespace SchulnetzSync.Core.Model;

/// <summary>
/// Represents a single event parsed from the Schulnetz iCal feed.
/// </summary>
/// <param name="Key">
/// The stable correlation key extracted from the UID, e.g. "P_65100" or "T_7409".
/// Does not change when an event is rescheduled — use this for upsert, not <paramref name="RawUid"/>.
/// </param>
/// <param name="RawUid">The full iCal UID, kept for diagnostic purposes only.</param>
/// <param name="Type">Event classification derived from the UID prefix.</param>
/// <param name="Start">Event start time, always in Europe/Zurich local time as an offset.</param>
/// <param name="End">
/// Event end time. For all-day events calculated from DTSTART + DURATION
/// (the feed omits DTEND for all-day entries).
/// </param>
/// <param name="IsAllDay">True when the feed entry uses VALUE=DATE (no time component).</param>
/// <param name="Summary">Human-readable title from the SUMMARY field.</param>
/// <param name="Location">Room/location, or null when the LOCATION field is empty.</param>
public sealed record SchulnetzEvent(
    string Key,
    string RawUid,
    SchulnetzEventType Type,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string Summary,
    string? Location);
