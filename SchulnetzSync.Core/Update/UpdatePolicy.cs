namespace SchulnetzSync.Core.Update;

/// <summary>
/// Decides whether to bother the user about an update. Pure functions: no
/// clock, no files, no network — the time comes in as a parameter so the rules
/// are testable.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>
    /// Days to wait after "Später erinnern".
    ///
    /// Bewusst kurz und immer gleich: Ein Update soll nicht in Vergessenheit
    /// geraten. Ein dringendes Update wartet ohnehin nicht auf diese Frist —
    /// siehe die Mandatory-Prüfung in <see cref="Decide"/>, die vor der Frist
    /// greift.
    /// </summary>
    private const int SnoozeDays = 1;

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
    /// True when <paramref name="offered"/> is a higher version than
    /// <paramref name="current"/>.
    ///
    /// Der Store liefert als Version des Updates teils die bereits
    /// installierte. Eine Zahl, die nicht höher ist als die laufende, darf
    /// darum nicht als «Version X verfügbar» angezeigt werden.
    /// Unlesbare Angaben gelten als nicht neuer.
    /// </summary>
    public static bool IsNewerThan(string? offered, string? current)
        => TryParse(offered, out var a) && TryParse(current, out var b) && a > b;

    private static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        // "2.2.0" und "2.2.0.0" sollen vergleichbar sein
        if (!Version.TryParse(text.Trim(), out var parsed)) return false;

        version = new Version(parsed.Major,
                              parsed.Minor,
                              Math.Max(parsed.Build, 0),
                              Math.Max(parsed.Revision, 0));
        return true;
    }

    /// <summary>
    /// The state after the user picked "Später erinnern": ask again tomorrow.
    ///
    /// Der Zähler wird mitgeführt, steuert die Frist aber nicht — jedes
    /// Verschieben kostet gleich viel. Eine neue Versionsnummer setzt ihn
    /// zurück, damit er beschreibt, wie oft genau diese Version verschoben
    /// wurde.
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
