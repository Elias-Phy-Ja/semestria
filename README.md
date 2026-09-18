<div align="center">

<img src="docs/logo.png" width="96" alt="Semestria Logo" />

# Semestria

**Schulnetz-Prüfungen und Termine automatisch in Outlook.**

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Herunterladen-0078d4?logo=microsoft&logoColor=white)](https://apps.microsoft.com/detail/9NJV8F0X7XMZ)

</div>

---

Semestria liest Prüfungen und Schultermine aus dem persönlichen Schulnetz-iCal-Feed, schreibt sie automatisch in den InApp Kalender und optional im  Outlook-Kalender. Ausserdem hat man die Möglichkeit Aufgaben einzutragen und kann so ein Wichtiges Werkzeug sein.

## Screenshots

<div align="center">

<img src="Pictures/Screenshot%202026-09-17%20200344.png" width="860" alt="Dashboard von Semestria" />

<sub><b>Dashboard</b> — Stand des letzten Abgleichs, die Zahlen des Semesters, was als Nächstes ansteht und der Stundenplan von morgen.</sub>

</div>

<table>
<tr>
<td width="50%"><img src="Pictures/Screenshot%202026-09-17%20200437.png" alt="Wochenansicht" /><br/>
<sub><b>Wochenansicht</b> — der Stundenplan als Raster. Prüfungen rot, Termine orange, jedes Fach in seiner eigenen Farbe.</sub></td>
<td width="50%"><img src="Pictures/Screenshot%202026-09-17%20200500.png" alt="Monatsansicht" /><br/>
<sub><b>Monatsansicht</b> — der ganze Monat auf einen Blick, volle Tage mit "+N mehr".</sub></td>
</tr>
<tr>
<td width="50%"><img src="Pictures/Screenshot%202026-09-17%20201204.png" alt="Aufgaben" /><br/>
<sub><b>Aufgaben</b> — nach Fach gruppiert, mit Abgabe und Erinnerung. Wichtiges steht zuoberst.</sub></td>
<td width="50%"><img src="Pictures/Screenshot%202026-09-17%20201258.png" alt="Einrichtung" /><br/>
<sub><b>Einrichtung</b> — fünf Schritte, bis der Feed läuft. Outlook ist dabei freiwillig.</sub></td>
</tr>
</table>

**[→ Alle Screenshots ansehen](Pictures/README.md)**


## Features

- 📅 Synchronisiert Prüfungen und Termine direkt aus dem Schulnetz-iCal
- 🔒 Feed-URL wird verschlüsselt lokal gespeichert (Windows DPAPI)
- 🗓️ Schreibt Events in den Outlook-Kalender via Microsoft Graph API
- 🖥️ Läuft im Hintergrund als System-Tray-App

## Download

**[→ Semestria im Microsoft Store](https://apps.microsoft.com/detail/9NJV8F0X7XMZ)**

Alternativ: `.msix`-Datei unter [Releases](../../releases) (Sideloading erforderlich).

## Tech Stack

| | |
|---|---|
| UI | WPF · .NET 9 · ModernWpfUI |
| Kalender | Microsoft Graph API v5 · MSAL |
| iCal | Ical.Net 5 |
| Sicherheit | DPAPI (lokale Verschlüsselung) |
| Distribution | MSIX · Microsoft Store |

## Changelog

### 17.09.2026 (v2.2)
- [x] Dashboard neu: Stundenplan, Kennzahlen, Aufgaben, einklappbares Protokoll
- [x] Einrichtung neu, inkl. Hinweis auf schulNetz.mobile
- [x] Kalender: Kopfleiste aufgeräumt, Fachkürzel statt voller Titel
- [x] Farben: 28 Farben, Farbmischer, einzelne Stunden und Termine färbbar
- [x] Kommentare pro Fach
- [x] Aufgaben: eigene Listenfarben, neues Design, Uhrzeiten wie getippt
- [x] Link zum Synapkey-Dashboard in der Seitenleiste
- [x] Standard neu: dunkles Design, nur Prüfungen nach Outlook
- [x] Fix: Absturz des Dashboards ab dem 18. Oktober (Zeitumstellung)
- [x] Fix: blaue Kachel hinter dem Taskleisten-Icon

### 04.09.2026
- [x] Outlook-Kalender-Integration (Microsoft Graph)
- [x] Titel kürzen (max. Zeichenlimit)
- [x] Filter verbessern

### 28.08.2026
- [x] Projekt geplant und Architektur definiert
- [x] iCal-Parser und Event-Klassifikation (Prüfung / Termin)
- [x] Feed-URL-Verschlüsselung mit DPAPI
- [x] App im Microsoft Store veröffentlicht

### 11.09.2026
- [x] Scroll bug beheben
- [x] Loading screen
- [x] Versions abruf um Updatebeachrichtigung zu ermöglichen (Updater)

*Summary: Beim Starten der Applikation lädt ein loading screen der Microsoft Store anfragt ob ein Update verfügbar ist um dies anzuzeigen.*

### 17.09.2026
- [x] Aufgaben Eintrag überprüfen
- [x] Listenfarbe ändern unabhängig von Stundenplan
- [x] Deeplink für meine Web-Applikation [Synapkey](https://synapkey.ch)

*Summary: Heute ist sehr viel geschehen, von Design überarbeitung bis CoreLogik änderungen. 
[Details](Session%20Summary/2026-09-17-v2.2.md)*