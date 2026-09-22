# Notes to Certification

Text zum Einfuegen im Partner Center unter
**Supplemental info > Additional Testing Information**.

Hintergrund: Die Einreichung vom 21.09.2026 ist an Punkt **10.3.3 App Is
Testable** gescheitert. Die Tester bei Microsoft haben kein Schulnetz-Konto,
landen darum im Einrichtungsassistenten bei Schritt 3 und sehen nie die
eigentliche Funktion. Am Paket muss dafuer nichts geaendert werden, es fehlt
nur ein Testzugang.

## Drei Eigenheiten des Formulars

Daran ist das Speichern nacheinander gescheitert:

1. Das Feld **Description** nimmt hoechstens **2000 Zeichen**. Die erste Fassung
   hatte 2132.
2. Sonderzeichen wie der Geviertstrich koennen das Speichern verhindern. Der
   Text ist darum reines ASCII, die deutschen Beschriftungen stehen ohne
   Umlaute da.
3. **Eine URL in der Description wird abgewiesen.** Der Link gehoert deshalb in
   die **Credentials**-Tabelle darunter, und der Text verweist nur dorthin.
   Im Text steht bewusst nirgends mehr die Zeichenfolge http.

## Credentials-Tabelle

Ueber **+ New credential** eine Zeile anlegen:

| Spalte | Wert |
|---|---|
| Name | `Test feed URL` |
| Value | `https://raw.githubusercontent.com/Elias-Phy-Ja/semestria/main/SchulnetzSync.Tests/Fixtures/sample-feed.ics` |

Weist auch dieses Feld die URL ab: das `https://` weglassen und in den Namen
schreiben, also Name `Test feed URL (prefix with https)` und Value
`raw.githubusercontent.com/Elias-Phy-Ja/semestria/main/SchulnetzSync.Tests/Fixtures/sample-feed.ics`.

---

## Description, zum Kopieren (1823 Zeichen)

Semestria is a calendar app for students in Switzerland. It reads exams and
school events from the student's personal timetable feed (an iCal subscription
link issued by the school portal "Schulnetz") and shows them in the app.
Copying entries into Outlook is optional.

Testers have no school account, so the app shows nothing until a feed link is
entered. A public test feed is provided in the Credentials section below under
the name "Test feed URL". It contains anonymised sample data, needs no login
and can be pasted straight into the app.

HOW TO TEST (about one minute):
1. Start the app. A five-step setup wizard opens. Click the blue button at the
   bottom right ("Los geht's").
2. Step 2: tick both checkboxes, then click the blue button ("Weiter").
3. Step 3: paste the test feed URL from the Credentials section into the input
   field, then click "Weiter".
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
- The package declares internet client access only, used to download the
  calendar feed over an encrypted connection.

---

## Hinweis zur Test-URL

Sie zeigt auf `SchulnetzSync.Tests/Fixtures/sample-feed.ics` im Branch `main`
dieses Repos und funktioniert, solange das Repo oeffentlich ist und die Datei
dort liegt. Wird sie je verschoben, bricht der Testzugang bei der naechsten
Zertifizierung. Wer das ausschliessen will, verlinkt statt `main` einen festen
Commit-Hash.

Geprueft am 22.09.2026: HTTP 200, 5822 Bytes, 20 VEVENTs. Ein Dry-Run der CLI
gegen diese URL ergibt 12 Eintraege, also 6 Pruefungen und 6 Termine.
