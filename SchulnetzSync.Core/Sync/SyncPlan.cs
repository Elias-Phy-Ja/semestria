namespace SchulnetzSync.Core.Sync;

/// <summary>
/// Everything the diff engine worked out for one run. With a non-empty
/// <see cref="Blockers"/> list the plan must not be executed.
/// </summary>
public sealed record SyncPlan(
    /// <summary>All actions. Still computed when there are blockers, so we can show them.</summary>
    IReadOnlyList<SyncAction> Actions,

    /// <summary>Why this plan must not run. Empty means it is safe.</summary>
    IReadOnlyList<string> Blockers)
{
    /// <summary>True when the plan may be executed.</summary>
    public bool CanExecute => Blockers.Count == 0;

    // Counters for the dry-run summary.
    public int CreateCount    => Actions.Count(a => a.Kind == SyncActionKind.Create);
    public int UpdateCount    => Actions.Count(a => a.Kind == SyncActionKind.Update);
    public int DeleteCount    => Actions.Count(a => a.Kind is SyncActionKind.Delete or SyncActionKind.MarkCancelled);
    public int FlagCount      => Actions.Count(a => a.Kind == SyncActionKind.FlagMissing);
    public int ClearCount     => Actions.Count(a => a.Kind == SyncActionKind.ClearMissing);
    public int DuplicateCount => Actions.Count(a => a.Kind == SyncActionKind.DeleteDuplicate);
}
