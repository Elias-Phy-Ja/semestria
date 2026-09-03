using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Sync;

/// <summary>
/// Represents a calendar event that was previously written by SchulnetzSync
/// and is now tracked via its extended properties.
/// </summary>
public sealed record TrackedEvent(
    /// <summary>The Graph calendar event ID (opaque string from Microsoft Graph).</summary>
    string CalendarEventId,

    /// <summary>The stable Schulnetz key, e.g. "P_65100".</summary>
    string Key,

    /// <summary>Event type as stored in the schulnetzType extended property.</summary>
    SchulnetzEventType Type,

    /// <summary>Content hash stored at write time; used to detect changes.</summary>
    string Hash,

    /// <summary>Event start time; used to determine if the event is in the past or feed window.</summary>
    DateTimeOffset Start,

    /// <summary>
    /// Set when the event is no longer found in the feed.
    /// Cleared when it reappears. Deletion happens after 24 h.
    /// </summary>
    DateTimeOffset? MissingSince);
