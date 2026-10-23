# semestria.elias-wyss.ch auf Hostinger

Der Ordner `docs/` ist die ganze Website: Startseite mit Store-Link (`index.html`),
Datenschutz (`policy.html`), die Nachrichtendatei der App (`news.json`) und die
Admin-Seite (`admin/`). Diese Datei selbst wird vom Server nicht ausgeliefert.

## Einmalig einrichten

1. **Subdomain anlegen:** hPanel → Domains → Subdomains → `semestria` für `elias-wyss.ch`.
   Ordner z. B. `public_html/semestria`.
2. **SSL:** hPanel → Sicherheit → SSL → für `semestria.elias-wyss.ch` aktivieren.
   Ohne HTTPS verweigern App und Admin-Seite den Dienst.
3. **PHP-Version:** hPanel → Erweitert → PHP-Konfiguration → **8.1 oder neuer**.
4. **Dateien hochladen** (Dateimanager oder FTP) in den Subdomain-Ordner:
   `index.html`, `policy.html`, `news.json`, `logo.png`, `favicon.ico`, `.htaccess`
   und den ganzen Ordner `admin/` (mit `admin/.htaccess` und `admin/data/.htaccess`).
   Versteckte Dateien (`.htaccess`) müssen mit, im Dateimanager «versteckte Dateien zeigen».
5. **config.php anlegen:** auf dem Server `admin/config.example.php` nach
   `admin/config.php` kopieren und bei `SETUP_TOKEN` eine lange Zufallszeichenkette
   eintragen (z. B. 40 Zeichen aus einem Passwortmanager). Diese Datei nie ins Repo.
6. **Schreibrechte:** `news.json` und der Ordner `admin/data/` müssen für PHP schreibbar
   sein (bei Hostinger normalerweise schon so).
7. **Passwort setzen:** `https://semestria.elias-wyss.ch/admin/` öffnen, Setup-Token
   eingeben und ein Admin-Passwort mit mindestens 14 Zeichen festlegen.
   Danach `SETUP_TOKEN` in `config.php` wieder leeren.

## Prüfen, dass nichts offen liegt

Diese Adressen müssen **403** oder **404** liefern, nicht den Inhalt:

- `https://semestria.elias-wyss.ch/admin/config.php`
- `https://semestria.elias-wyss.ch/admin/lib.php`
- `https://semestria.elias-wyss.ch/admin/data/admin.json`
- `https://semestria.elias-wyss.ch/admin/data/attempts.json`
- `https://semestria.elias-wyss.ch/HOSTINGER.md`

Und `http://semestria.elias-wyss.ch/` muss auf `https://` umleiten.

## Nachrichten schreiben

Auf `/admin/` anmelden, Titel und Text eingeben, bei einer Umfrage den Formular-Link
(https) und z. B. «Zur Umfrage» als Button-Text. «Sichtbar ab» leer heisst sofort.
Die App holt `news.json` ungefähr 10 Sekunden nach dem Start und zeigt jede Nachricht
nur einmal (die ID merkt sie sich lokal). Darum bleibt die ID nach dem Erstellen fest.

## Sicherheit, kurz

- Passwort nur als `password_hash` in `admin/data/admin.json`, Ordner per `.htaccess` gesperrt.
- Erstes Passwort nur mit dem Setup-Token aus `config.php`.
- Nach 5 Fehlversuchen pro IP (oder 25 insgesamt) 15 Minuten Sperre.
- Sitzungs-Cookie nur über HTTPS, nicht für Skripte lesbar, SameSite=Strict, 30 Minuten Leerlauf.
- Jedes Formular mit CSRF-Token, alle Eingaben gegen feste Regeln geprüft, Ausgaben escaped.
- Strenge Content-Security-Policy, kein JavaScript auf der Admin-Seite.
- Die App öffnet nur `https://`-Links aus Nachrichten.
