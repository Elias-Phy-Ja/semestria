# Notes to Certification

Text zum Einfuegen im Partner Center unter
**Supplemental info > Additional Testing Information > Notes for Certification**,
Feld **Description**.

Hintergrund: Die Einreichung vom 21.09.2026 ist an Punkt **10.3.3 App Is
Testable** gescheitert. Die Tester bei Microsoft haben kein Schulnetz-Konto,
landen darum im Einrichtungsassistenten bei Schritt 3 und sehen nie die
eigentliche Funktion. Der Text unten gibt ihnen einen oeffentlichen Testfeed mit
anonymisierten Daten und fuehrt sie durch die deutschsprachige Oberflaeche.

Am Paket selbst muss dafuer nichts geaendert werden.

Zwei Einschraenkungen des Feldes, an denen der erste Versuch gescheitert ist:
es nimmt hoechstens 2000 Zeichen, und Sonderzeichen wie der Geviertstrich
koennen das Speichern verhindern. Der Text unten hat 1771 Zeichen und ist
reines ASCII. Deshalb stehen die deutschen Beschriftungen ohne Umlaute da, und
die Anleitung nennt zusaetzlich die Position der Schaltflaechen, damit der
Tester sie trotzdem eindeutig findet.

---

## Zum Kopieren (Englisch)

Semestria is a calendar app for students in Switzerland. It reads exams and
school events from the student's personal timetable feed (an iCal subscription
link issued by the school portal "Schulnetz") and shows them in the app.
Copying entries into Outlook is optional.

Testers have no school account, so the app shows nothing until a feed link is
entered. Please use this public test feed. It contains anonymised sample data
and requires no credentials:

https://raw.githubusercontent.com/Elias-Phy-Ja/semestria/main/SchulnetzSync.Tests/Fixtures/sample-feed.ics

HOW TO TEST (about one minute):
1. Start the app. A five-step setup wizard opens. Click the blue button at the
   bottom right ("Los geht's").
2. Step 2: tick both checkboxes, then click the blue button ("Weiter").
3. Step 3: paste the test feed URL into the input field, then "Weiter".
4. Step 4 (Outlook) is optional. Click the SECOND of the two large cards, the
   one that starts with "Spaeter". No Microsoft account is needed to test.
5. Step 5: click the blue button at the bottom right to finish. The main
   window opens and the sample entries are loaded.

WHAT TO LOOK AT (left menu, top to bottom):
- 2nd item, the calendar: month and week view with the sample entries. This is
  the primary functionality of the app.
- 1st item, the dashboard: upcoming exams and today's timetable.
- 3rd item, tasks: local to-do lists. Add one with the input field at the top.

NOTES:
- No backend, no account system, no telemetry. All data stays on the device.
- The user interface is in German.
- The Outlook connection (Microsoft Graph, delegated Calendars.ReadWrite) is
  optional and is not needed for any of the above.
- The package declares internet client access only, to download the feed
  over HTTPS.

---

## Hinweis zur Test-URL

Die URL zeigt auf `SchulnetzSync.Tests/Fixtures/sample-feed.ics` im Branch
`main` dieses Repos. Sie funktioniert, solange das Repo oeffentlich ist und die
Datei dort liegt. Wird sie je verschoben oder umbenannt, bricht der Testzugang
bei der naechsten Zertifizierung. Wer das ausschliessen will, verlinkt statt
`main` einen festen Commit-Hash.

Geprueft am 22.09.2026: HTTP 200, 5822 Bytes, 20 VEVENTs. Ein Dry-Run der CLI
gegen diese URL ergibt 12 Eintraege, also 6 Pruefungen und 6 Termine.
