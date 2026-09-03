namespace SchulnetzSync.Core.Sync;

/// <summary>
/// The complete output of the diff engine for one sync run.
/// If <see cref="Blockers"/> is non-empty the plan must NOT be executed.
/// </summary>
public sealed record SyncPlan(
    /// <summary>All actions, computed even when blockers are present (for display).</summary>
    IReadOnlyList<SyncAction> Actions,

    /// <summary>
    /// Reasons why this plan must not be executed.
    /// Empty = safe to execute.
    /// </summary>
    IReadOnlyList<string> Blockers)
{
    /// <summary>True when the plan may be safely executed.</summary>
    public bool CanExecute => Blockers.Count == 0;

    /// <summary>Shortcut counts for the dry-run summary.</summary>
    public int CreateCount    => Actions.Count(a => a.Kind == SyncActionKind.Create);
    public int UpdateCount    => Actions.Count(a => a.Kind == SyncActionKind.Update);
    public int DeleteCount    => Actions.Count(a => a.Kind is SyncActionKind.Delete or SyncActionKind.MarkCancelled);
    public int FlagCount      => Actions.Count(a => a.Kind == SyncActionKind.FlagMissing);
    public int ClearCount     => Actions.Count(a => a.Kind == SyncActionKind.ClearMissing);
}
