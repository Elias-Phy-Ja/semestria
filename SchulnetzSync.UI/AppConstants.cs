namespace SchulnetzSync.UI;

/// <summary>
/// App-weite Konstanten. Die ClientId muss vor dem Veröffentlichen
/// durch die echte Azure-App-Registrierung ersetzt werden.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// Azure-App-Registrierung (portal.azure.com → App registrations).
    /// Delegated permission: Calendars.ReadWrite
    /// Platform: Public client/native, Redirect: http://localhost
    /// </summary>
    public const string ClientId = "YOUR-CLIENT-ID-HERE";

    public const string AppName    = "Semestria";
    public const string Version    = "1.0.0";
    public const string Publisher  = "Elias Wyss";

    /// <summary>Versionsnummer des akzeptierten Rechtsdokuments.</summary>
    public const int LegalVersion = 1;

    /// <summary>GitHub-Link für Feedback / Issues.</summary>
    public const string GitHubUrl = "https://github.com/Elias-Phy-Ja/schulnetzsync";
}
