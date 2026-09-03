using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Sync;

/// <summary>
/// A single action produced by the diff engine.
/// Exactly one of <see cref="Source"/> and <see cref="Existing"/> may be null,
/// depending on the action kind.
/// </summary>
public sealed record SyncAction(
    SyncActionKind Kind,

    /// <summary>The feed event driving this action. Null for Delete/FlagMissing.</summary>
    SchulnetzEvent? Source,

    /// <summary>The currently tracked calendar event. Null for Create.</summary>
    TrackedEvent? Existing,

    /// <summary>Human-readable explanation, shown in dry-run output.</summary>
    string Reason);
