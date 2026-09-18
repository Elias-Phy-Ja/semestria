namespace SchulnetzSync.Core.Update;

/// <summary>
/// Decides whether to bother the user about an update. Pure functions: no clock,
/// no files, no network — the time comes in as a parameter so the rules are testable.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>
    /// Days to wait after "Später erinnern".
    ///
    /// Short and always the same on purpose, so an update cannot quietly be forgotten.
    /// An urgent one does not wait for this anyway — the mandatory check in
    /// <see cref="Decide"/> runs before the deadline is even looked at.
    /// </summary>
    private const int SnoozeDays = 1;

    /// <summary>Decides what the loading view should show.</summary>
    /// <param name="updates">What the store offers; empty means nothing to do.</param>
    /// <param name="prefs">The snooze state from disk.</param>
    /// <param name="nowUtc">Current time, injected so the tests stay deterministic.</param>
    public static UpdateDecision Decide(
        IReadOnlyList<UpdateInfo> updates,
        UpdatePreferences         prefs,
        DateTimeOffset            nowUtc)
    {
        if (updates.Count == 0)
            return new UpdateDecision(UpdatePrompt.None);

        // With several packages (the app plus optional ones) the message names the
        // first, which for a single-package app is its own version.
        var version = updates[0].Version;

        // Mandatory beats any postponement.
        if (updates.Any(u => u.IsMandatory))
            return new UpdateDecision(UpdatePrompt.Mandatory, version);

        // A different version than the postponed one: the deadline was for the old one.
        bool sameVersion = string.Equals(prefs.SkippedVersion, version, StringComparison.Ordinal);
        if (!sameVersion)
            return new UpdateDecision(UpdatePrompt.Optional, version);

        // Same version, still inside the snooze window.
        if (prefs.RemindAfterUtc is { } until && nowUtc < until)
            return new UpdateDecision(UpdatePrompt.None);

        // Same version, window expired or never set.
        return new UpdateDecision(UpdatePrompt.Optional, version);
    }
    /// <summary>
    /// True when <paramref name="offered"/> really is higher than <paramref name="current"/>.
    ///
    /// The store sometimes hands back the version that is already installed, and a number
    /// that is not higher than the running one must not turn into "Version X available".
    /// Anything unparseable counts as not newer.
    /// </summary>
    public static bool IsNewerThan(string? offered, string? current)
        => TryParse(offered, out var a) && TryParse(current, out var b) && a > b;

    private static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (!Version.TryParse(text.Trim(), out var parsed)) return false;

        // Pad the missing parts so "2.2.0" and "2.2.0.0" compare equal.
        version = new Version(parsed.Major,
                              parsed.Minor,
                              Math.Max(parsed.Build, 0),
                              Math.Max(parsed.Revision, 0));
        return true;
    }

    /// <summary>
    /// The state after the user picked "Später erinnern": ask again tomorrow.
    ///
    /// The counter comes along but does not steer the deadline — every postponement
    /// costs the same. A new version number resets it, so it always says how often
    /// this particular version was pushed away.
    /// </summary>
    public static UpdatePreferences Postpone(
        UpdatePreferences prefs,
        string            version,
        DateTimeOffset    nowUtc)
    {
        bool sameVersion = string.Equals(prefs.SkippedVersion, version, StringComparison.Ordinal);
        int  next        = (sameVersion ? prefs.PostponeCount : 0) + 1;

        return new UpdatePreferences(version, nowUtc.AddDays(SnoozeDays), next);
    }
}
