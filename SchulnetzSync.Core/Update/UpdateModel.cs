namespace SchulnetzSync.Core.Update;

/// <summary>A package update offered by the store.</summary>
/// <param name="Version">Version of the offered package, e.g. "2.1.0.0".</param>
/// <param name="IsMandatory">
/// The store flagged it as mandatory. Those are offered no matter what the user
/// postponed earlier.
/// </param>
public sealed record UpdateInfo(string Version, bool IsMandatory);

/// <summary>
/// Persisted snooze state. Deliberately tiny — it is the only thing that survives a
/// restart, and a corrupt file must never be able to block the start.
/// </summary>
/// <param name="SkippedVersion">Version the user last postponed, null if never.</param>
/// <param name="RemindAfterUtc">
/// Earliest time to ask again. Null means ask on every start, which is where an update
/// that was postponed over and over ends up.
/// </param>
/// <param name="PostponeCount">How often this version was postponed.</param>
public sealed record UpdatePreferences(
    string?         SkippedVersion = null,
    DateTimeOffset? RemindAfterUtc = null,
    int             PostponeCount  = 0);

/// <summary>How an installation ended, when the call came back at all.</summary>
public enum InstallOutcome
{
    /// <summary>Installed underneath the running app, so it has to restart itself.</summary>
    NeedsRestart,
}

/// <summary>What the loading view should do with the update check.</summary>
public enum UpdatePrompt
{
    /// <summary>Carry on into the app.</summary>
    None,

    /// <summary>Offer the update with a way to postpone it.</summary>
    Optional,

    /// <summary>Offer the update without a way out.</summary>
    Mandatory,
}

/// <param name="Prompt">What to show.</param>
/// <param name="Version">Version to name in the prompt, null when nothing is shown.</param>
/// <param name="FailureReason">
/// Why the check produced nothing — timeout, offline, no store context. Belongs in the
/// log and never in front of the user: a failed check is not their problem and must not
/// hold up the start.
/// </param>
public sealed record UpdateDecision(
    UpdatePrompt Prompt,
    string?      Version       = null,
    string?      FailureReason = null);
