# Notes to Certification

Text zum Einfügen im Partner Center unter «Notes to Certification», bevor die
nächste Einreichung abgeschickt wird.

Hintergrund: Die Einreichung vom 21.09.2026 ist an Punkt **10.3.3 App Is
Testable** gescheitert. Die Tester bei Microsoft haben kein Schulnetz-Konto,
landen darum im Einrichtungsassistenten bei Schritt 3 und kommen nicht weiter.
Der Text unten gibt ihnen einen öffentlichen Testfeed mit anonymisierten Daten
und führt sie Schritt für Schritt durch die deutschsprachige Oberfläche.

Am Paket selbst muss dafür nichts geändert werden.

---

## Zum Kopieren (Englisch)

Semestria is a calendar app for students in Switzerland. It reads exams and
school events from the student's personal timetable feed — an iCal subscription
link issued by the school portal ("Schulnetz") — and displays them in the app.
It can optionally copy those entries into the user's Outlook calendar.

Testers do not have a school account, so the app has nothing to display until a
feed link is entered. Please use the public test feed below. It contains
anonymised sample data and requires no credentials.

TEST FEED URL (public, no login required):
https://raw.githubusercontent.com/Elias-Phy-Ja/semestria/main/SchulnetzSync.Tests/Fixtures/sample-feed.ics

HOW TO TEST (about one minute):
1. Start the app. A five-step setup wizard opens. Click "Los geht's".
2. Step 2 "Bedingungen": tick both checkboxes, then click "Weiter".
3. Step 3 "Schulnetz verbinden": paste the test feed URL above into the input
   field, then click "Weiter".
4. Step 4 "Outlook" is optional and labelled as such. Click
   "Später, erst mal ohne Outlook". No Microsoft account is needed for testing.
5. Step 5: click the button at the bottom right to finish. The main window opens
   and the sample entries are loaded.

WHAT TO LOOK AT:
- "Dashboard" — summary of upcoming exams and events, and today's timetable.
- "Prüfungen & Termine" — the calendar in month and week view, showing the
  sample entries. This is the primary functionality of the app.
- "Aufgaben" — local to-do lists. A task can be added with the input field at
  the top of the page.
- "Einstellungen" — feed link, light/dark theme, and which entry types are
  written to Outlook.

ADDITIONAL NOTES:
- The app has no backend, no server of ours, no account system and no
  telemetry. All data stays on the device.
- The user interface is in German. The app is aimed at students in
  German-speaking Switzerland.
- The Outlook connection (Microsoft Graph, delegated Calendars.ReadWrite) is
  entirely optional. None of the functionality listed above requires it.
- The only capability the package declares is internet client access, used to
  download the calendar feed over HTTPS.

---

## Hinweis zur Test-URL

Die URL zeigt auf `SchulnetzSync.Tests/Fixtures/sample-feed.ics` im Branch
`main` dieses Repos. Sie funktioniert, solange das Repo öffentlich ist und die
Datei dort liegt. Wird sie je verschoben oder umbenannt, bricht der Testzugang
bei der nächsten Zertifizierung. Wer das ausschliessen will, verlinkt statt
`main` einen festen Commit-Hash — dieselbe URL mit dem Hash an Stelle von `main`
bleibt für immer gültig.

Geprüft am 22.09.2026: HTTP 200, 5822 Bytes, 20 VEVENTs. Ein Dry-Run der CLI
gegen diese URL ergibt 12 Einträge, also 6 Prüfungen und 6 Termine.
