namespace SchulnetzSync.Core.Update;

/// <summary>
/// Decides whether to bother the user about an update. Pure functions: no
/// clock, no files, no network — the time comes in as a parameter so the rules
/// are testable.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>Days to wait after the first postponement.</summary>
    private const int FirstSnoozeDays = 3;

    /// <summary>Days to wait after the second one.</summary>
    private const int SecondSnoozeDays = 7;

    /// <summary>
    /// Decides what the loading view should show.
    /// </summary>
    /// <param name="updates">What the store offers; empty means nothing to do.</param>
    /// <param name="prefs">The snooze state from disk.</param>
    /// <param name="nowUtc">Current time, injected for deterministic tests.</param>
    public static UpdateDecision Decide(
        IReadOnlyList<UpdateInfo> updates,
        UpdatePreferences         prefs,
        DateTimeOffset            nowUtc)
    {
        if (updates.Count == 0)
            return new UpdateDecision(UpdatePrompt.None);

        // Bei mehreren Paketen (App plus optionale) benennt die Meldung das
        // erste — für eine App mit einem Paket ist das ihre eigene Version.
        var version = updates[0].Version;

        // Ein zwingendes Update sticht jede Verschiebung.
        if (updates.Any(u => u.IsMandatory))
            return new UpdateDecision(UpdatePrompt.Mandatory, version);

        // Eine andere Version als die verschobene: die Frist galt der alten.
        bool sameVersion = string.Equals(prefs.SkippedVersion, version, StringComparison.Ordinal);
        if (!sameVersion)
            return new UpdateDecision(UpdatePrompt.Optional, version);

        // Gleiche Version, Frist läuft noch.
        if (prefs.RemindAfterUtc is { } until && nowUtc < until)
            return new UpdateDecision(UpdatePrompt.None);

        // Gleiche Version, Frist abgelaufen oder keine mehr gesetzt.
        return new UpdateDecision(UpdatePrompt.Optional, version);
    }

    /// <summary>
    /// The state after the user picked "Später erinnern": 3 days, then 7, then
    /// on every start.
    ///
    /// Eine neue Versionsnummer setzt den Zähler zurück — sonst würde eine
    /// frische Version sofort im seltensten Intervall landen.
    /// </summary>
    public static UpdatePreferences Postpone(
        UpdatePreferences prefs,
        string            version,
        DateTimeOffset    nowUtc)
    {
        bool sameVersion = string.Equals(prefs.SkippedVersion, version, StringComparison.Ordinal);
        int  next        = (sameVersion ? prefs.PostponeCount : 0) + 1;

        DateTimeOffset? remindAfter = next switch
        {
            1 => nowUtc.AddDays(FirstSnoozeDays),
            2 => nowUtc.AddDays(SecondSnoozeDays),
            _ => null,   // ab dem dritten Mal bei jedem Start fragen
        };

        return new UpdatePreferences(version, remindAfter, next);
    }
}
