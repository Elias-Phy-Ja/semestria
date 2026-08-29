namespace SchulnetzSync.Core.Model;

/// <summary>
/// Controls which event types are synced and how conflicts are resolved.
/// </summary>
public sealed class SyncOptions
{
    /// <summary>
    /// The set of event types to synchronise on this run.
    /// Only <see cref="SchulnetzEventType.Pruefung"/> and
    /// <see cref="SchulnetzEventType.Termin"/> are valid members;
    /// <see cref="SchulnetzEventType.Lektion"/> is always silently excluded.
    /// </summary>
    public IReadOnlySet<SchulnetzEventType> EnabledTypes { get; init; }
        = new HashSet<SchulnetzEventType> { SchulnetzEventType.Pruefung, SchulnetzEventType.Termin };

    /// <summary>
    /// Target calendar ID. Null means the user's primary calendar.
    /// </summary>
    public string? CalendarId { get; init; }

    /// <summary>
    /// When true, a disappeared exam is marked "[Abgesagt] …" instead of being deleted.
    /// Only applies to <see cref="SchulnetzEventType.Pruefung"/>.
    /// </summary>
    public bool CancelInsteadOfDelete { get; init; } = true;

    /// <summary>
    /// When true, the diff engine attempts to fill a missing exam room
    /// from a lesson that starts at the same time.
    /// </summary>
    public bool EnrichExamLocationFromLesson { get; init; } = true;
}
