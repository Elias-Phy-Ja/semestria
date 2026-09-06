namespace SchulnetzSync.UI;

/// <summary>
/// App-weite Konstanten.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// Application (client) ID of the app registration that ships with Semestria.
    /// Users sign in against this registration and never touch a Microsoft portal.
    ///
    /// Eine Client-ID ist kein Geheimnis: Dies ist ein Public Client mit PKCE,
    /// die ID ist zum Ausliefern gedacht. Ein Client Secret gibt es hier nicht.
    ///
    /// Registrierung "Semestria.61bc8af998c4", angelegt am 3.9.2026 unter dem
    /// persönlichen Konto. Sie liegt in KEINEM Verzeichnis (Legacy-Registrierung
    /// im Consumer-Kontext), daher gilt:
    ///
    ///   • Nur persönliche Microsoft-Konten können sich anmelden
    ///     (outlook.com, hotmail.com, live.com oder eine beliebige Adresse,
    ///     die als privates MS-Konto registriert ist).
    ///   • Schul- und Geschäftskonten funktionieren NICHT.
    ///   • Microsoft hat das Anlegen solcher Registrierungen als veraltet
    ///     markiert. Bestehende laufen weiter, neue lassen sich so nicht
    ///     mehr erstellen.
    ///
    /// Wenn Schulkonten unterstützt werden sollen: eigenen Entra-Mandanten
    /// anlegen (setzt ein Azure-Konto mit hinterlegter Zahlungsmethode voraus,
    /// Entra ID Free kostet trotzdem nichts), die App dort neu registrieren mit
    /// Kontotyp "Konten in einem beliebigen Organisationsverzeichnis und
    /// persönliche Microsoft-Konten" und die neue ID hier eintragen. Das ergibt
    /// eine neue Client-ID; bestehende Nutzer müssen sich einmal neu anmelden.
    ///
    /// Benötigte Einstellungen der Registrierung:
    ///   • Authentifizierung: Plattform "Mobile Geräte und Desktopcomputer",
    ///     Redirect http://localhost, öffentlicher Client aktiviert
    ///   • API-Berechtigungen: Microsoft Graph, delegiert, Calendars.ReadWrite
    /// </summary>
    public const string ClientId = "437f085a-2ec7-4dbd-aecd-46cd1468a268";

    public const string AppName    = "Semestria";
    /// <summary>
    /// Angezeigte Version. Muss zur Version in Packaging\Package.appxmanifest
    /// passen (dort vierstellig: 2.0.0.0).
    /// </summary>
    public const string Version    = "2.0.0";

    /// <summary>Kurzform für die Seitenleiste, z.B. "v2.0".</summary>
    public const string VersionShort = "v2.0";
    public const string Publisher  = "Elias Wyss";

    /// <summary>Versionsnummer des akzeptierten Rechtsdokuments.</summary>
    public const int LegalVersion = 1;

    /// <summary>GitHub-Link für Feedback / Issues.</summary>
    public const string GitHubUrl = "https://github.com/Elias-Phy-Ja/semestria";
}
