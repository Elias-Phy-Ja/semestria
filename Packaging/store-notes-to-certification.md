# Notes to Certification

Text fuer das Partner Center unter
**Supplemental info > Additional Testing Information**.

## Stand

Zwei Ablehnungen, beide mit demselben Punkt **10.3.3 App Is Testable**:

| Review | Ergebnis |
|---|---|
| 21.09.2026 | abgelehnt, 10.3.3 |
| 24.09.2026 | abgelehnt, 10.3.3, getestet auf einem Microsoft Surface Laptop |

Der zweite Bericht ist wortgleich mit dem ersten. Zwei Erklaerungen kommen in
Frage, und sie schliessen sich nicht aus:

1. Die Notes waren bei der zweiten Einreichung gar nicht gespeichert. Das
   Formular hatte dreimal abgelehnt, zuletzt wegen der URL im Textfeld.
2. Der Tester wollte die beworbene Outlook-Funktion pruefen und meldete sich
   mit einem **Geschaeftskonto** an. Das kann nicht klappen: die
   App-Registrierung ist vom Typ PersonalMicrosoftAccount, die Anmeldung
   laeuft gegen den consumers-Endpunkt, und ein Arbeits- oder Schulkonto wird
   abgewiesen. Der Tester sieht eine Fehlermeldung und kommt nicht weiter.

Der Text unten stellt darum beides gleich zu Beginn klar: der Kalender
braucht ueberhaupt kein Konto, und die Outlook-Anmeldung funktioniert nur mit
einem privaten Microsoft-Konto.

Version 3.0 (24.10.2026): Text unten erweitert um Rundgang, Noten, Profile und
den Abruf der Nachrichtendatei.

## Eigenheiten des Formulars

1. **Description** nimmt hoechstens **2000 Zeichen**. Der Text unten (v3.0) hat
   1947, mit Zeilenumbruechen als zwei Zeichen gerechnet 1981.
2. Sonderzeichen koennen das Speichern verhindern. Der Text ist reines ASCII,
   deutsche Beschriftungen darum ohne Umlaute.
3. **Eine URL in der Description wird abgewiesen.** Der Link gehoert in die
   Credentials-Tabelle, im Text steht nirgends die Zeichenfolge http.

## Credentials-Tabelle

Ueber **+ New credential**:

| Spalte | Wert |
|---|---|
| Name | `Test feed URL` |
| Value | `https://raw.githubusercontent.com/Elias-Phy-Ja/semestria/main/SchulnetzSync.Tests/Fixtures/sample-feed.ics` |

Weist auch dieses Feld die URL ab: `https://` weglassen und in den Namen
schreiben.

---

## Description, zum Kopieren

Semestria is a calendar and school companion app for students in Switzerland.
It reads exams, school events and lessons from the student's personal school
timetable feed (an iCal subscription link) and displays them. Writing entries
to Outlook is optional. Version 3.0 adds grades, profiles and short guided tours.

PLEASE READ BEFORE TESTING:
- The main functions need NO account. They only need the test feed link, found
  in the Credentials section below under the name "Test feed URL". It holds
  anonymised sample data and needs no login.
- The optional Outlook sign-in accepts PERSONAL Microsoft accounts only
  (outlook.com, hotmail.com, live.com). Work and school accounts are rejected
  by design. Step 4 of the setup can be skipped.

HOW TO TEST:
1. Start the app. A five-step wizard opens. Click the blue button at the
   bottom right ("Los geht's").
2. Step 2: tick both checkboxes, then the blue button ("Weiter").
3. Step 3: paste the test feed URL into the input field, then "Weiter".
4. Step 4 (Outlook, optional): click the SECOND large card ("Spaeter").
5. Step 5: click the blue button. The main window opens.
6. A short guided tour appears. Click "Weiter" to follow it or
   "Ueberspringen" to close it (Esc works too). Each page has its own short
   tour on the first visit, closed the same way.

WHAT TO LOOK AT (left menu):
- Calendar ("Pruefungen & Termine"): month and week view with the sample data.
- Dashboard: upcoming exams and today's timetable.
- Grades ("Noten"): first pick a rule card (e.g. "Pluspunkte"), then start a
  semester with "+ Semester / Jahr starten" and add a grade.
- Profile switcher at the top left: profiles are local, no account involved.

PRIVACY: no account system, no telemetry, all user data stays on the device.
Besides the feed, the app only reads a public news file from the developer's
website (no personal data sent, can be switched off in the settings). The user interface is German.

---

## Hinweis zur Test-URL

Sie zeigt auf `SchulnetzSync.Tests/Fixtures/sample-feed.ics` im Branch `main`.
Geprueft am 22.09.2026: HTTP 200, 5822 Bytes, 20 VEVENTs. Ein Dry-Run der CLI
dagegen ergibt 12 Eintraege, also 6 Pruefungen und 6 Termine.
