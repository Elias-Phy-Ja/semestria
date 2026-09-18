namespace SchulnetzSync.Core.Model;

/// <summary>Which event types a run touches, and how it handles the awkward cases.</summary>
public sealed class SyncOptions
{
    /// <summary>
    /// Types to sync on this run. Only Pruefung and Termin mean anything here —
    /// Lektion is dropped no matter what the caller passes in.
    /// </summary>
    public IReadOnlySet<SchulnetzEventType> EnabledTypes { get; init; }
        = new HashSet<SchulnetzEventType> { SchulnetzEventType.Pruefung, SchulnetzEventType.Termin };

    /// <summary>Target calendar. Null means the primary calendar of the account.</summary>
    public string? CalendarId { get; init; }

    /// <summary>
    /// Rename a vanished exam to "[Abgesagt] …" instead of deleting it. Exams only:
    /// a cancelled exam is news worth keeping, a vanished appointment usually is not.
    /// </summary>
    public bool CancelInsteadOfDelete { get; init; } = true;

    /// <summary>
    /// Fill a missing exam room from the lesson starting at the same time.
    /// The feed leaves the room off exams often enough to make this worth doing.
    /// </summary>
    public bool EnrichExamLocationFromLesson { get; init; } = true;
}
