using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Sync;

/// <summary>
/// One action the diff engine wants carried out. Which of <see cref="Source"/> and
/// <see cref="Existing"/> is null depends on the kind.
/// </summary>
public sealed record SyncAction(
    SyncActionKind Kind,

    /// <summary>The feed event behind this action. Null for Delete and FlagMissing.</summary>
    SchulnetzEvent? Source,

    /// <summary>The calendar event we already track. Null for Create.</summary>
    TrackedEvent? Existing,

    /// <summary>Plain-language reason, shown in the dry-run output.</summary>
    string Reason);
