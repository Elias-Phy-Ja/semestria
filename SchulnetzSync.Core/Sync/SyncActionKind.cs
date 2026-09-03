namespace SchulnetzSync.Core.Sync;

/// <summary>The kind of action the diff engine wants to perform.</summary>
public enum SyncActionKind
{
    /// <summary>Event is new in the feed — create it in the calendar.</summary>
    Create,

    /// <summary>Event exists in both but its content changed — update the calendar entry.</summary>
    Update,

    /// <summary>Event has been missing for 24 h+ — delete the calendar entry.</summary>
    Delete,

    /// <summary>
    /// Exam has been missing for 24 h+ and CancelInsteadOfDelete is on —
    /// set the title to "[Abgesagt] …" instead of deleting.
    /// </summary>
    MarkCancelled,

    /// <summary>
    /// Event disappeared from the feed for the first time —
    /// stamp schulnetzMissingSince; do not delete yet.
    /// </summary>
    FlagMissing,

    /// <summary>Event reappeared in the feed — clear the schulnetzMissingSince stamp.</summary>
    ClearMissing,
}
