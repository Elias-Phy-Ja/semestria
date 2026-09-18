namespace SchulnetzSync.Core.Sync;

/// <summary>What the diff engine wants to do with a single event.</summary>
public enum SyncActionKind
{
    /// <summary>New in the feed — create it.</summary>
    Create,

    /// <summary>Known on both sides but the content moved — update the calendar entry.</summary>
    Update,

    /// <summary>Gone from the feed for more than 24 h — delete it.</summary>
    Delete,

    /// <summary>
    /// Same as Delete, but for an exam while CancelInsteadOfDelete is on:
    /// retitle to "[Abgesagt] …" so the cancellation stays visible.
    /// </summary>
    MarkCancelled,

    /// <summary>
    /// First run in which the event is missing — only stamp schulnetzMissingSince.
    /// Nothing is deleted yet, because feeds hiccup.
    /// </summary>
    FlagMissing,

    /// <summary>Back in the feed — clear the schulnetzMissingSince stamp.</summary>
    ClearMissing,

    /// <summary>
    /// Two calendar entries carry the same key, so drop the surplus copy. Separate from
    /// <see cref="Delete"/> because it cleans up our own mistake instead of reacting to
    /// the feed, and must not count towards the mass-deletion safeguard.
    /// </summary>
    DeleteDuplicate,
}
