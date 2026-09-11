namespace SchulnetzSync.Core.Update;

/// <summary>A package update offered by the store.</summary>
/// <param name="Version">Version string of the offered package, e.g. "2.1.0.0".</param>
/// <param name="IsMandatory">
/// True when the store marks the update as mandatory. A mandatory update is
/// offered regardless of any snooze the user set earlier.
/// </param>
public sealed record UpdateInfo(string Version, bool IsMandatory);

/// <summary>
/// Persisted snooze state. Kept deliberately small — it is the only thing that
/// survives a restart, and a corrupt file must never block startup.
/// </summary>
/// <param name="SkippedVersion">Version the user last postponed, null if never.</param>
/// <param name="RemindAfterUtc">
/// Earliest time to ask again. Null means ask on every start, which is where a
/// repeatedly postponed update ends up.
/// </param>
/// <param name="PostponeCount">How often this version was postponed.</param>
public sealed record UpdatePreferences(
    string?         SkippedVersion = null,
    DateTimeOffset? RemindAfterUtc = null,
    int             PostponeCount  = 0);

/// <summary>What the loading view should do with the update check.</summary>
public enum UpdatePrompt
{
    /// <summary>Carry on into the app.</summary>
    None,

    /// <summary>Offer the update with a way to postpone it.</summary>
    Optional,

    /// <summary>Offer the update without a way to postpone it.</summary>
    Mandatory,
}

/// <param name="Prompt">What to show.</param>
/// <param name="Version">Version to name in the prompt, null when nothing is shown.</param>
/// <param name="FailureReason">
/// Why the check produced no result — timeout, offline, no store context.
/// Belongs in the log, never in front of the user: a failed check is not their
/// problem and must not interrupt the start.
/// </param>
public sealed record UpdateDecision(
    UpdatePrompt Prompt,
    string?      Version       = null,
    string?      FailureReason = null);
