namespace SchulnetzSync.UI;

/// <summary>
/// The wording for linking a Microsoft account, in plain language.
///
/// Shared by the onboarding wizard and the settings page on purpose: this setup is the
/// hardest thing the app asks of anyone, and meeting two different descriptions of it
/// would be worse than meeting none.
/// </summary>
public static class OutlookSetupText
{
    /// <summary>One sentence on what the user gets out of it.</summary>
    public const string Benefit =
        "Semestria trägt deine Prüfungen und Termine zusätzlich in deinen Outlook-Kalender ein. " +
        "So siehst du sie auch auf dem Handy, in Teams und überall, wo du Outlook nutzt.";

    /// <summary>What they actually have to do, which is very little.</summary>
    public const string HowItWorks =
        "Du meldest dich einmal mit deinem Microsoft-Konto an und bestätigst, dass Semestria " +
        "deinen Kalender bearbeiten darf. Danach läuft alles automatisch, du musst nichts " +
        "einrichten und nichts eintragen.";

    /// <summary>What happens on a no — and it has to sound like a real option, because it is.</summary>
    public const string SkipConsequence =
        "Ohne Outlook funktioniert Semestria vollständig: Prüfungen und Termine erscheinen " +
        "im Kalender in der App. Du kannst Outlook jederzeit später in den Einstellungen verknüpfen.";

    /// <summary>
    /// Only on the advanced path, where someone registers an app of their own.
    /// Normal users never get here.
    /// </summary>
    public const string AccountWarning =
        "Nimm dafür ein privates Microsoft-Konto (@outlook.com, @hotmail.com oder @live.com). " +
        "Mit dem Schulkonto klappt es meistens nicht, weil Schulen das Registrieren von Apps meist sperren.";

    /// <summary>
    /// The click-by-click walkthrough, advanced path only. Every step names exactly what
    /// to click and quotes the portal labels so they can be found on screen. Registering
    /// an app is free — no Azure subscription, no credit card.
    /// </summary>
    public static string[] Steps { get; } =
    [
        "1.  Unten auf «Microsoft-Portal öffnen» klicken und mit dem privaten Konto anmelden. Das ist gratis, es braucht kein Azure-Abo und keine Kreditkarte.",
        "2.  Falls die Meldung kommt, dein Konto sei im Mandanten «Microsoft Services» nicht vorhanden: oben «Microsoft Entra ID» suchen → «Mandanten verwalten» → «Erstellen» → «Microsoft Entra ID». Das legt dir ein leeres, kostenloses Verzeichnis an. Privaten Konten fehlt das anfangs.",
        "3.  Oben in der Suchleiste «App-Registrierungen» eingeben und das Ergebnis anklicken.",
        "4.  Auf «Neue Registrierung» klicken.",
        "5.  Als Name «Semestria» eintragen. Darunter «Nur persönliche Microsoft-Konten» auswählen und auf «Registrieren» klicken.",
        "6.  Auf der Übersichtsseite die «Anwendungs-ID (Client)» kopieren und hier oben einfügen.",
        "7.  Links auf «API-Berechtigungen» → «Berechtigung hinzufügen» → «Microsoft Graph» → «Delegierte Berechtigungen». Dort «Calendars.ReadWrite» suchen, anhaken und hinzufügen.",
        "8.  Links auf «Authentifizierung» → «Plattform hinzufügen» → «Mobile Geräte und Desktopcomputer». Die erste Option anhaken und auf «Konfigurieren» klicken.",
        "9.  Zurück in Semestria auf «Mit Microsoft anmelden» klicken. Fertig."
    ];
}
