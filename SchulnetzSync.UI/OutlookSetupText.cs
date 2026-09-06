namespace SchulnetzSync.UI;

/// <summary>
/// Plain-language instructions for linking a Microsoft account.
/// Shared by the onboarding wizard and the settings page so both explain the
/// same procedure with the same words — the setup is the hardest part of the
/// app and users should not meet two different descriptions of it.
/// </summary>
public static class OutlookSetupText
{
    /// <summary>One sentence on what the user gets out of it.</summary>
    public const string Benefit =
        "Semestria trägt deine Prüfungen und Termine zusätzlich in deinen Outlook-Kalender ein. " +
        "So siehst du sie auch auf dem Handy, in Teams und überall, wo du Outlook nutzt.";

    /// <summary>What the user actually has to do — which is very little.</summary>
    public const string HowItWorks =
        "Du meldest dich einmal mit deinem Microsoft-Konto an und bestätigst, dass Semestria " +
        "deinen Kalender bearbeiten darf. Danach läuft alles automatisch — du musst nichts " +
        "einrichten und nichts eintragen.";

    /// <summary>What happens if the user says no.</summary>
    public const string SkipConsequence =
        "Ohne Outlook funktioniert Semestria vollständig: Prüfungen und Termine erscheinen " +
        "im Kalender in der App. Du kannst Outlook jederzeit später in den Einstellungen verknüpfen.";

    /// <summary>
    /// Only relevant on the advanced path, where the user registers their own
    /// app. Normal users never see this.
    /// </summary>
    public const string AccountWarning =
        "Nimm dafür ein privates Microsoft-Konto (@outlook.com, @hotmail.com oder @live.com). " +
        "Mit dem Schulkonto klappt es meistens nicht — Schulen sperren das Registrieren von Apps.";

    /// <summary>
    /// The click-by-click walkthrough for the advanced path only. Every entry
    /// names exactly what to click, with the portal labels in quotes so they can
    /// be found on screen. App registration is free — no Azure subscription and
    /// no credit card are involved.
    /// </summary>
    public static string[] Steps { get; } =
    [
        "1.  Unten auf «Microsoft-Portal öffnen» klicken und mit dem privaten Konto anmelden. Das ist gratis — es braucht kein Azure-Abo und keine Kreditkarte.",
        "2.  Falls die Meldung kommt, dein Konto sei im Mandanten «Microsoft Services» nicht vorhanden: oben «Microsoft Entra ID» suchen → «Mandanten verwalten» → «Erstellen» → «Microsoft Entra ID». Das legt dir ein leeres, kostenloses Verzeichnis an. Privaten Konten fehlt das anfangs.",
        "3.  Oben in der Suchleiste «App-Registrierungen» eingeben und das Ergebnis anklicken.",
        "4.  Auf «Neue Registrierung» klicken.",
        "5.  Als Name «Semestria» eintragen. Darunter «Nur persönliche Microsoft-Konten» auswählen und auf «Registrieren» klicken.",
        "6.  Auf der Übersichtsseite die «Anwendungs-ID (Client)» kopieren und hier oben einfügen.",
        "7.  Links auf «API-Berechtigungen» → «Berechtigung hinzufügen» → «Microsoft Graph» → «Delegierte Berechtigungen». Dort «Calendars.ReadWrite» suchen, anhaken und hinzufügen.",
        "8.  Links auf «Authentifizierung» → «Plattform hinzufügen» → «Mobile Geräte und Desktopcomputer». Die erste Option anhaken und auf «Konfigurieren» klicken.",
        "9.  Zurück in Semestria auf «Mit Microsoft anmelden» klicken — fertig."
    ];
}
