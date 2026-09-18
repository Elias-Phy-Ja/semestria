namespace SchulnetzSync.Core.Model;

/// <summary>One event as parsed from the Schulnetz feed.</summary>
/// <param name="Key">
/// Correlation key taken from the UID, e.g. "P_65100". Survives rescheduling, so always
/// match on this and never on <paramref name="RawUid"/>.
/// </param>
/// <param name="RawUid">The full UID, kept for diagnostics only.</param>
/// <param name="Type">Classification derived from the UID prefix.</param>
/// <param name="Start">Start time, always Europe/Zurich as an offset.</param>
/// <param name="End">
/// End time. All-day entries carry no DTEND in this feed, so it is DTSTART + DURATION.
/// </param>
/// <param name="IsAllDay">Feed entry used VALUE=DATE, so it has no time component.</param>
/// <param name="Summary">Title from the SUMMARY field.</param>
/// <param name="Location">Room, or null when LOCATION was empty.</param>
public sealed record SchulnetzEvent(
    string Key,
    string RawUid,
    SchulnetzEventType Type,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string Summary,
    string? Location);
