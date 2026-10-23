namespace SchulnetzSync.UI.Legal;

/// <summary>
/// The legal texts, embedded as plain strings so they work offline and cannot go missing.
/// German text in Swiss spelling: ss, never ß.
///
/// Changing their substance means raising <see cref="AppConstants.LegalVersion"/>, which
/// makes everyone accept them again on the next start (see LegalAcceptWindow), and
/// updating docs/policy.html, which is what the Store links to.
/// </summary>
public static class LegalTexts
{
    /// <summary>Shown above the texts when an update asks for consent again.</summary>
    public const string ChangesSummary =
        "Neu kann Semestria Nachrichten und Umfrage-Links vom Entwickler anzeigen. Dafür lädt " +
        "die App kurz nach dem Start eine öffentliche Datei, ohne Angaben über dich zu senden; " +
        "in den Einstellungen lässt sich das ausschalten. Ausserdem regeln die Texte jetzt, dass " +
        "allfällige spätere Cloud-Funktionen nur mit deiner ausdrücklichen Zustimmung genutzt " +
        "werden. Bitte lies die neuen Fassungen und akzeptiere sie.";

    public const string Agb = """
        NUTZUNGSBEDINGUNGEN – Semestria
        Version 2.0 · Stand: Oktober 2026
        ════════════════════════════════════

        1. GELTUNGSBEREICH
        Diese Nutzungsbedingungen gelten für die Verwendung der App «Semestria»
        (nachfolgend «App»). Mit der Verwendung der App erklärst du dich mit diesen
        Bedingungen einverstanden.

        2. LEISTUNGSBESCHREIBUNG
        Semestria ist eine kostenlose, quelloffene Desktop-App für Windows. Sie
        liest deinen persönlichen Schulnetz-Kalender, zeigt ihn in der App an und
        schreibt auf Wunsch Prüfungen sowie Schultermine in deinen Microsoft
        Outlook-Kalender. Dazu kommen Aufgaben, Notizen und Noten, die du selbst
        einträgst. Deine Daten bleiben auf deinem Gerät.

        3. KOSTENLOSE NUTZUNG
        Die App wird kostenlos zur Verfügung gestellt. Es besteht kein Anspruch auf
        Support, Updates oder Weiterentwicklung.

        4. HAFTUNGSAUSSCHLUSS
        Die App wird «so wie sie ist» bereitgestellt, ohne jegliche Garantie. Der
        Entwickler übernimmt keine Haftung für:
        · Fehler bei der Synchronisation (fehlende, falsche oder doppelte Einträge)
        · Verlust von Kalenderdaten, Aufgaben oder Noten
        · falsch berechnete Notenschnitte, Pluspunkte oder Promotionsprognosen
        · Schäden, die durch die Nutzung oder Nicht-Nutzung der App entstehen
        · Unterbrüche der Verfügbarkeit

        Es liegt in deiner Verantwortung, die Daten auf Korrektheit zu prüfen,
        besonders vor wichtigen Prüfungen. Massgebend sind immer die Angaben deiner
        Schule, nicht die Berechnungen der App.

        5. SCHULNETZ-ZUGANGSDATEN
        Die Feed-URL enthält einen persönlichen Zugangscode (Token). Du bist selbst
        verantwortlich für die sichere Aufbewahrung deiner Zugangsdaten. Teile deine
        Feed-URL mit niemandem.

        6. MICROSOFT-KONTO
        Die App greift mit deinem expliziten Einverständnis auf deinen
        Microsoft Outlook-Kalender zu. Du kannst diesen Zugriff jederzeit unter
        account.microsoft.com/privacy widerrufen.

        7. NACHRICHTEN UND UMFRAGEN
        Der Entwickler kann über die App kurze Nachrichten anzeigen, etwa Hinweise
        zu neuen Versionen oder Links zu Umfragen. Die Teilnahme an Umfragen ist
        freiwillig. Umfragen laufen über externe Formulardienste, für die deren
        eigene Bedingungen gelten. Nachrichten lassen sich in den Einstellungen
        ausschalten.

        8. OPTIONALE CLOUD-FUNKTIONEN
        Zurzeit bietet Semestria keine Cloud-Funktionen an. Sollten später solche
        dazukommen (zum Beispiel Zugriff über eine Web-App), sind sie freiwillig und
        erst nach deiner ausdrücklichen, separaten Zustimmung aktiv. Ohne diese
        Zustimmung funktioniert die App weiterhin vollständig lokal. Für die
        Cloud-Funktionen gelten dann ergänzende Bedingungen, die dir vor der
        Aktivierung angezeigt werden.

        9. OPEN SOURCE
        Der Quellcode steht unter der MIT-Lizenz auf GitHub zur Verfügung.
        Du darfst den Code verwenden, verändern und weitergeben, sofern du die
        Lizenzbedingungen einhältst.

        10. ÄNDERUNGEN
        Der Entwickler behält sich vor, diese Nutzungsbedingungen zu ändern. Bei
        wesentlichen Änderungen zeigt die App beim nächsten Start die neue Fassung
        an und fragt erneut nach deiner Zustimmung.

        11. ANWENDBARES RECHT
        Es gilt schweizerisches Recht. Gerichtsstand ist Bern, Schweiz.

        ════════════════════════════════════
        Entwickler: Elias Wyss · Semestria
        """;

    public const string Datenschutz = """
        DATENSCHUTZERKLÄRUNG – Semestria
        Version 2.0 · Stand: Oktober 2026
        ════════════════════════════════════

        1. VERANTWORTLICHER
        Elias Wyss («Entwickler»)
        Die App ist ein privates Non-Profit-Projekt.

        2. GRUNDSATZ: DEINE DATEN BLEIBEN BEI DIR
        Semestria verarbeitet deine Daten ausschliesslich lokal auf deinem Gerät.
        Es gibt:
        · Keine Telemetrie
        · Keine Nutzungsstatistiken
        · Keine Tracking-Dienste
        · Keine Werbung
        · Kein Benutzerkonto und keinen Server, der deine Daten speichert

        3. WELCHE DATEN WERDEN VERARBEITET?

        3.1 Feed-URL (Schulnetz-Kalender)
        Deine Feed-URL wird verschlüsselt auf deinem Gerät gespeichert.
        Speicherort: %LOCALAPPDATA%\Semestria\profiles\<Profil>\config.json
        Die Verschlüsselung erfolgt mit dem Windows-Datenschutz-API (DPAPI),
        das an dein Windows-Benutzerkonto gebunden ist. Niemand ausser dir
        (auf diesem Gerät) kann die URL entschlüsseln.
        Die URL verlässt dein Gerät nur, um den Schulnetz-Server direkt abzufragen.

        3.2 Microsoft-Zugangstoken
        Nach der Anmeldung speichert die App ein Zugriffstoken für Microsoft Graph.
        Speicherort: %LOCALAPPDATA%\Semestria\profiles\<Profil>\token_cache.bin
        Das Token verlässt dein Gerät nur, um API-Anfragen an Microsoft Graph zu
        stellen (Lesen/Schreiben deines Outlook-Kalenders). Du kannst den Zugriff
        jederzeit unter account.microsoft.com/privacy entziehen.

        3.3 Einstellungen, Aufgaben, Notizen und Noten
        Einstellungen, Aufgaben, Kommentare, Farben, eigene Termine und Noten
        speichert die App nur lokal in %LOCALAPPDATA%\Semestria\, für jedes
        Profil in einem eigenen Unterordner. Profile auf demselben PC sehen
        die Daten der anderen nicht.
        Diese Daten werden nicht übertragen.

        3.4 Kalenderdaten
        Die App liest Kalendereinträge von deinem Schulnetz-Feed und schreibt sie
        auf Wunsch in deinen Outlook-Kalender via Microsoft Graph API. Diese Daten
        werden von Microsoft gemäss deren Datenschutzrichtlinie
        (privacy.microsoft.com) verarbeitet.

        3.5 Nachrichten vom Entwickler
        Etwa zehn Sekunden nach dem Start lädt die App die öffentliche Datei
        https://semestria.elias-wyss.ch/news.json. Dabei sendet die App keine
        Angaben über dich, keine Kennung und keine Daten aus der App. Wie bei
        jedem Webseitenaufruf erhält der Server deine IP-Adresse und technische
        Angaben zur Anfrage (Zeitpunkt, angefragte Datei). Die Seite liegt beim
        Webhoster Hostinger, der solche Zugriffe gemäss seiner Datenschutz-
        erklärung kurzzeitig protokollieren kann. Der Entwickler wertet diese
        Protokolle nicht aus. Welche Nachrichten du schon gesehen hast, speichert
        die App nur lokal. Den Abruf kannst du in den Einstellungen ausschalten.

        3.6 Umfragen
        Ein Link zu einer Umfrage öffnet sich in deinem Browser. Was du dort
        angibst, verarbeitet der jeweilige Formulardienst nach seiner eigenen
        Datenschutzerklärung. Die Teilnahme ist freiwillig; die App überträgt
        nichts an das Formular.

        3.7 Updates
        Ob eine neue Version bereitsteht, fragt die App beim Microsoft Store ab.

        4. OPTIONALE CLOUD-FUNKTIONEN
        Zurzeit gibt es keine. Sollten später Cloud-Funktionen angeboten werden,
        gilt: Sie sind freiwillig, werden erst nach deiner ausdrücklichen Zustimmung
        aktiviert, und vor der Aktivierung erfährst du genau, welche Daten wohin
        gehen, wie sie verschlüsselt sind und wie du sie wieder löschst. Ohne
        Zustimmung bleibt alles wie in Abschnitt 2 beschrieben.

        5. DRITTANBIETER-DIENSTE
        · Microsoft Graph API (microsoft.com) – für Kalender-Zugriff
        · Microsoft Store – für Updates
        · Schulnetz/Centerboard – für deinen Stundenplan-Feed
        · Hostinger – Webhosting der Nachrichtendatei
        Für die Datenschutzpraktiken dieser Anbieter sind wir nicht verantwortlich.

        6. DEINE RECHTE
        Du kannst jederzeit:
        · Alle lokalen App-Daten löschen (Ordner %LOCALAPPDATA%\Semestria\ entfernen)
        · Den Microsoft-Zugriff unter account.microsoft.com widerrufen
        · Den Nachrichtenabruf in den Einstellungen ausschalten
        · Die App deinstallieren

        7. DATENSICHERHEIT
        · Feed-URL: DPAPI-verschlüsselt
        · Microsoft-Token: DPAPI-verschlüsselt (via MSAL Cache Helper)
        · Verbindungen zu Schulnetz, Microsoft und dem Nachrichtenserver nur über HTTPS
        · Keine Übertragung deiner Daten an Dritte ausser Microsoft und Schulnetz

        8. KINDER UND JUGENDLICHE
        Die App richtet sich an Schülerinnen und Schüler. Sie verarbeitet keine
        besonderen Kategorien personenbezogener Daten.

        9. KONTAKT
        Bei Fragen: github.com/Elias-Phy-Ja/semestria/issues

        10. ÄNDERUNGEN
        Bei wesentlichen Änderungen zeigt die App beim nächsten Start die neue
        Fassung an und fragt erneut nach deiner Zustimmung.

        ════════════════════════════════════
        Entwickler: Elias Wyss · Semestria
        """;
}
