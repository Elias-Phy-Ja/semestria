namespace SchulnetzSync.UI.Legal;

/// <summary>
/// Rechtsdokumente als eingebettete Strings.
/// Schweizer Rechtschreibung: ss statt ß.
/// </summary>
public static class LegalTexts
{
    public const string Agb = """
        NUTZUNGSBEDINGUNGEN – Semestria
        Version 1.0 · Stand: September 2026
        ════════════════════════════════════

        1. GELTUNGSBEREICH
        Diese Nutzungsbedingungen gelten für die Verwendung der App «Semestria»
        (nachfolgend «App»). Mit der Verwendung der App erklärst du dich mit diesen
        Bedingungen einverstanden.

        2. LEISTUNGSBESCHREIBUNG
        Semestria ist eine kostenlose, quelloffene Desktop-App für Windows. Sie
        liest deinen persönlichen Schulnetz-Kalender und schreibt Prüfungen sowie
        Schultermine in deinen Microsoft Outlook-Kalender. Die App funktioniert rein
        lokal auf deinem Gerät – es werden keine Daten an externe Server übertragen.

        3. KOSTENLOSE NUTZUNG
        Die App wird kostenlos zur Verfügung gestellt. Es besteht kein Anspruch auf
        Support, Updates oder Weiterentwicklung.

        4. HAFTUNGSAUSSCHLUSS
        Die App wird «so wie sie ist» bereitgestellt, ohne jegliche Garantie. Der
        Entwickler übernimmt keine Haftung für:
        · Fehler bei der Synchronisation (fehlende, falsche oder doppelte Einträge)
        · Verlust von Kalenderdaten
        · Schäden, die durch die Nutzung oder Nicht-Nutzung der App entstehen
        · Unterbrüche der Verfügbarkeit

        Es liegt in deiner Verantwortung, die synchronisierten Daten auf Korrektheit
        zu prüfen, besonders vor wichtigen Prüfungen.

        5. SCHULNETZ-ZUGANGSDATEN
        Die Feed-URL enthält ein persönliches Zugangscode (Token). Du bist selbst
        verantwortlich für die sichere Aufbewahrung deiner Zugangsdaten. Teile deine
        Feed-URL mit niemandem.

        6. MICROSOFT-KONTO
        Die App greift mit deinem expliziten Einverständnis auf deinen
        Microsoft Outlook-Kalender zu. Du kannst diesen Zugriff jederzeit unter
        account.microsoft.com/privacy widerrufen.

        7. OPEN SOURCE
        Der Quellcode steht unter der MIT-Lizenz auf GitHub zur Verfügung.
        Du darfst den Code verwenden, verändern und weitergeben, sofern du die
        Lizenzbedingungen einhältst.

        8. ÄNDERUNGEN
        Der Entwickler behält sich vor, diese Nutzungsbedingungen jederzeit zu ändern.
        Bei wesentlichen Änderungen wird beim nächsten Start die aktualisierte Version
        angezeigt.

        9. ANWENDBARES RECHT
        Es gilt schweizerisches Recht. Gerichtsstand ist Bern, Schweiz.

        ════════════════════════════════════
        Entwickler: Elias Wyss · Semestria
        """;

    public const string Datenschutz = """
        DATENSCHUTZERKLÄRUNG – Semestria
        Version 1.0 · Stand: September 2026
        ════════════════════════════════════

        1. VERANTWORTLICHER
        Elias Wyss («Entwickler»)
        Die App ist ein privates Non-Profit-Projekt.

        2. GRUNDSATZ: KEINE CLOUD, KEINE TELEMETRIE
        Semestria verarbeitet alle Daten ausschliesslich lokal auf deinem Gerät.
        Es gibt:
        · Keine Telemetrie
        · Keine Nutzungsstatistiken
        · Keine Tracking-Dienste
        · Keine Werbung
        · Keinen eigenen Backend-Server

        3. WELCHE DATEN WERDEN VERARBEITET?

        3.1 Feed-URL (Schulnetz-Kalender)
        Deine Feed-URL wird verschlüsselt auf deinem Gerät gespeichert.
        Speicherort: %LOCALAPPDATA%\Semestria\config.json
        Die Verschlüsselung erfolgt mit dem Windows-Datenschutz-API (DPAPI),
        das an dein Windows-Benutzerkonto gebunden ist. Niemand ausser dir
        (auf diesem Gerät) kann die URL entschlüsseln.
        Die URL verlässt dein Gerät nur um den Schulnetz-Server direkt abzufragen.

        3.2 Microsoft-Zugangstoken
        Nach der Anmeldung speichert die App ein Zugriffstoken für Microsoft Graph.
        Speicherort: %LOCALAPPDATA%\Semestria\token_cache.bin
        Das Token verlässt dein Gerät nur um API-Anfragen an Microsoft Graph zu stellen
        (Lesen/Schreiben deines Outlook-Kalenders). Du kannst den Zugriff jederzeit
        unter account.microsoft.com/privacy entziehen.

        3.3 Konfigurationsdaten
        Die App speichert Einstellungen (z.B. Kalender-ID, letzte Sync-Zeit) lokal:
        %LOCALAPPDATA%\Semestria\config.json
        Diese Daten werden nicht übertragen.

        3.4 Kalenderdaten
        Die App liest Kalendereinträge von deinem Schulnetz-Feed und schreibt sie in
        deinen Outlook-Kalender via Microsoft Graph API. Diese Daten werden von
        Microsoft gemäss deren Datenschutzrichtlinie (privacy.microsoft.com) verarbeitet.

        4. DRITTANBIETER-DIENSTE
        · Microsoft Graph API (microsoft.com) – für Kalender-Zugriff
        · Schulnetz/Centerboard – für deinen Stundenplan-Feed
        Für die Datenschutzpraktiken dieser Anbieter sind wir nicht verantwortlich.

        5. DEINE RECHTE
        Du kannst jederzeit:
        · Alle lokalen App-Daten löschen (Ordner %LOCALAPPDATA%\Semestria\ entfernen)
        · Den Microsoft-Zugriff unter account.microsoft.com widerrufen
        · Die App deinstallieren

        6. DATENSICHERHEIT
        · Feed-URL: DPAPI-verschlüsselt
        · Microsoft-Token: DPAPI-verschlüsselt (via MSAL Cache Helper)
        · Keine Übertragung an Dritte ausser Microsoft und Schulnetz

        7. KINDER
        Die App richtet sich an Schülerinnen und Schüler. Sie verarbeitet keine
        besonderen Kategorien personenbezogener Daten.

        8. KONTAKT
        Bei Fragen: github.com/Elias-Phy-Ja/semestria/issues

        9. ÄNDERUNGEN
        Bei wesentlichen Änderungen wird beim nächsten Start die neue Version angezeigt.

        ════════════════════════════════════
        Entwickler: Elias Wyss · Semestria
        """;
}
