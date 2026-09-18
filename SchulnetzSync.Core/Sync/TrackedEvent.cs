using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Sync;

/// <summary>
/// A calendar event we wrote earlier and now recognise again by its extended properties.
/// </summary>
public sealed record TrackedEvent(
    /// <summary>Opaque event id from Microsoft Graph.</summary>
    string CalendarEventId,

    /// <summary>The Schulnetz key, e.g. "P_65100".</summary>
    string Key,

    /// <summary>Type as stored in the schulnetzType property.</summary>
    SchulnetzEventType Type,

    /// <summary>Content hash from the last write — this is how we notice changes.</summary>
    string Hash,

    /// <summary>Start time, needed to decide whether the event is past or inside the feed window.</summary>
    DateTimeOffset Start,

    /// <summary>
    /// When the event first went missing from the feed; cleared once it is back.
    /// Deletion only follows 24 h later.
    /// </summary>
    DateTimeOffset? MissingSince);
