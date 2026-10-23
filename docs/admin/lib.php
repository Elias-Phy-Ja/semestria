<?php
// Shared helpers for the news admin. Not reachable on its own (see .htaccess).
//
// Security model, in short:
//  - one admin, password stored only as a password_hash() in data/admin.json
//  - the very first password needs SETUP_TOKEN from config.php, so nobody who finds the
//    page before you can claim it
//  - session cookie Secure + HttpOnly + SameSite=Strict, new id on login, idle timeout
//  - CSRF token on every POST
//  - lockout after repeated failed logins, per IP and in total
//  - every input validated against a whitelist; output always escaped
//  - news.json written atomically, so the app never reads half a file

declare(strict_types=1);

if (!is_file(__DIR__ . '/config.php')) {
    http_response_code(500);
    exit('config.php fehlt. Kopiere config.example.php nach config.php und fülle SETUP_TOKEN aus.');
}
require __DIR__ . '/config.php';

const SESSION_IDLE_SECONDS = 30 * 60;
const MAX_FAILS_PER_IP     = 5;
const MAX_FAILS_TOTAL      = 25;
const LOCKOUT_SECONDS      = 15 * 60;
const KINDS                = ['info' => 'Info', 'survey' => 'Umfrage', 'update' => 'Update'];
const TZ                   = 'Europe/Zurich';

// ── Output ──────────────────────────────────────────────────────────────

function h(?string $s): string
{
    return htmlspecialchars($s ?? '', ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}

function send_security_headers(): void
{
    header("Content-Security-Policy: default-src 'none'; style-src 'self'; img-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'");
    header('X-Content-Type-Options: nosniff');
    header('X-Frame-Options: DENY');
    header('Referrer-Policy: no-referrer');
    header('X-Robots-Tag: noindex, nofollow');
    header('Cache-Control: no-store');
}

function redirect_self(string $query = ''): never
{
    header('Location: ./' . ($query === '' ? '' : '?' . $query), true, 303);
    exit;
}

function flash(string $message, bool $error = false): void
{
    $_SESSION['flash'] = ['text' => $message, 'error' => $error];
}

function take_flash(): ?array
{
    $f = $_SESSION['flash'] ?? null;
    unset($_SESSION['flash']);
    return $f;
}

// ── Session and CSRF ────────────────────────────────────────────────────

function start_session(): void
{
    if (empty($_SERVER['HTTPS']) || $_SERVER['HTTPS'] === 'off') {
        http_response_code(403);
        exit('Nur über HTTPS.');
    }

    ini_set('session.use_strict_mode', '1');
    ini_set('session.use_only_cookies', '1');
    session_name('semestria_admin');
    session_set_cookie_params([
        'lifetime' => 0,
        'path'     => '/admin/',
        'secure'   => true,
        'httponly' => true,
        'samesite' => 'Strict',
    ]);
    session_start();

    $last = $_SESSION['last_seen'] ?? 0;
    if ($last !== 0 && time() - $last > SESSION_IDLE_SECONDS) {
        $_SESSION = [];
        session_regenerate_id(true);
        flash('Du wurdest nach 30 Minuten ohne Aktivität abgemeldet.');
    }
    $_SESSION['last_seen'] = time();
}

function is_logged_in(): bool
{
    return ($_SESSION['admin'] ?? false) === true;
}

function csrf_token(): string
{
    if (empty($_SESSION['csrf'])) {
        $_SESSION['csrf'] = bin2hex(random_bytes(32));
    }
    return $_SESSION['csrf'];
}

function require_csrf(): void
{
    $sent = $_POST['csrf'] ?? '';
    if (!is_string($sent) || !hash_equals(csrf_token(), $sent)) {
        http_response_code(400);
        exit('Ungültige Anfrage. Lade die Seite neu und versuche es noch einmal.');
    }
}

// ── Small JSON files in the data folder ─────────────────────────────────

function read_json_file(string $path): array
{
    if (!is_file($path)) return [];
    $data = json_decode((string)file_get_contents($path), true);
    return is_array($data) ? $data : [];
}

/** Writes via a temporary file and rename, so a reader never sees half a file. */
function write_json_file(string $path, array $data, int $flags = 0): void
{
    $dir = dirname($path);
    if (!is_dir($dir) && !mkdir($dir, 0750, true)) {
        throw new RuntimeException("Ordner $dir lässt sich nicht anlegen.");
    }
    $tmp = $path . '.' . bin2hex(random_bytes(6)) . '.tmp';
    $json = json_encode($data, $flags | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
    if (file_put_contents($tmp, $json . "\n", LOCK_EX) === false || !rename($tmp, $path)) {
        @unlink($tmp);
        throw new RuntimeException("$path lässt sich nicht schreiben.");
    }
}

// ── Password ────────────────────────────────────────────────────────────

function admin_file(): string
{
    return DATA_DIR . '/admin.json';
}

function stored_password_hash(): ?string
{
    $hash = read_json_file(admin_file())['hash'] ?? null;
    return is_string($hash) && $hash !== '' ? $hash : null;
}

function store_password_hash(string $password): void
{
    write_json_file(admin_file(), ['hash' => password_hash($password, PASSWORD_DEFAULT)]);
}

function password_problem(string $password, string $repeat): ?string
{
    if (strlen($password) < 14) return 'Das Passwort braucht mindestens 14 Zeichen.';
    if ($password !== $repeat)  return 'Die beiden Passwörter stimmen nicht überein.';
    return null;
}

// ── Lockout after failed logins ─────────────────────────────────────────

function attempts_file(): string
{
    return DATA_DIR . '/attempts.json';
}

/** The IP is only kept as a hash, and only for the lockout window. */
function client_key(): string
{
    return hash('sha256', ($_SERVER['REMOTE_ADDR'] ?? '') . '|' . SETUP_TOKEN . '|semestria');
}

function load_attempts(): array
{
    $now = time();
    // Forget failures older than the lockout window
    return array_filter(read_json_file(attempts_file()),
        fn($t) => is_int($t) && $now - $t < LOCKOUT_SECONDS);
}

/** Seconds until another login may be tried; 0 when it is allowed now. */
function lockout_remaining(): int
{
    $attempts = load_attempts();
    $mine = array_filter($attempts, fn($t, $k) => str_starts_with((string)$k, client_key()), ARRAY_FILTER_USE_BOTH);

    $blockedBy = null;
    if (count($mine) >= MAX_FAILS_PER_IP)    $blockedBy = $mine;
    if (count($attempts) >= MAX_FAILS_TOTAL) $blockedBy = $attempts;
    if ($blockedBy === null) return 0;

    return max(0, min($blockedBy) + LOCKOUT_SECONDS - time());
}

function record_failed_login(): void
{
    $attempts = load_attempts();
    $attempts[client_key() . '-' . bin2hex(random_bytes(4))] = time();
    write_json_file(attempts_file(), $attempts);
}

function clear_failed_logins(): void
{
    $attempts = array_filter(load_attempts(),
        fn($k) => !str_starts_with((string)$k, client_key()), ARRAY_FILTER_USE_KEY);
    write_json_file(attempts_file(), $attempts);
}

// ── News ────────────────────────────────────────────────────────────────

/** @return list<array> newest first */
function load_news(): array
{
    $feed  = read_json_file(NEWS_FILE);
    $items = is_array($feed['items'] ?? null) ? $feed['items'] : [];
    usort($items, fn($a, $b) => strcmp((string)($b['publishedAt'] ?? ''), (string)($a['publishedAt'] ?? '')));
    return array_values($items);
}

function save_news(array $items): void
{
    usort($items, fn($a, $b) => strcmp((string)($b['publishedAt'] ?? ''), (string)($a['publishedAt'] ?? '')));
    write_json_file(NEWS_FILE, ['version' => 1, 'items' => array_values($items)], JSON_PRETTY_PRINT);
}

/** "2026-10-24T08:00" from the form, Zurich time, to ISO 8601 with offset; false when malformed. */
function to_iso(string $local): string|false|null
{
    $local = trim($local);
    if ($local === '') return null;
    $dt = DateTimeImmutable::createFromFormat('Y-m-d\TH:i', $local, new DateTimeZone(TZ));
    return $dt === false ? false : $dt->format(DATE_ATOM);
}

/** The reverse, for filling the form. */
function to_local(?string $iso): string
{
    if (!$iso) return '';
    try {
        return (new DateTimeImmutable($iso))->setTimezone(new DateTimeZone(TZ))->format('Y-m-d\TH:i');
    } catch (Exception) {
        return '';
    }
}

function slug(string $s): string
{
    $s = strtr(mb_strtolower($s, 'UTF-8'), ['ä' => 'ae', 'ö' => 'oe', 'ü' => 'ue', 'é' => 'e', 'è' => 'e', 'à' => 'a']);
    $s = preg_replace('/[^a-z0-9]+/', '-', $s) ?? '';
    return trim(substr(trim($s, '-'), 0, 40), '-');
}

/**
 * Checks the form against a whitelist and builds the item exactly as the app reads it.
 * @return array{0: ?array, 1: list<string>} the item, or null with error messages
 */
function validate_item(array $in, ?string $editingId, array $existingIds): array
{
    $errors = [];
    $str = fn(string $k) => is_string($in[$k] ?? null) ? trim($in[$k]) : '';

    $title = $str('title');
    $body  = str_replace("\r\n", "\n", $str('body'));
    $kind  = $str('kind');
    $link  = $str('link');
    $label = $str('linkLabel');
    $min   = $str('minVersion');
    $max   = $str('maxVersion');

    if ($title === '' || mb_strlen($title) > 120) $errors[] = 'Titel fehlt oder ist länger als 120 Zeichen.';
    if (mb_strlen($body) > 2000)                  $errors[] = 'Der Text ist länger als 2000 Zeichen.';
    if (!array_key_exists($kind, KINDS))          $errors[] = 'Unbekannte Art.';
    if (mb_strlen($label) > 40)                   $errors[] = 'Die Button-Beschriftung ist länger als 40 Zeichen.';

    if ($link !== '') {
        $parts = parse_url($link);
        if (!is_array($parts) || ($parts['scheme'] ?? '') !== 'https' || empty($parts['host']) || strlen($link) > 500) {
            $errors[] = 'Der Link muss mit https:// beginnen.';
        }
    }

    foreach (['minVersion' => $min, 'maxVersion' => $max] as $name => $v) {
        if ($v !== '' && !preg_match('/^\d+(\.\d+){1,3}$/', $v)) $errors[] = "$name muss wie 2.3.0 aussehen.";
    }

    $published = to_iso($str('publishedAt'))
        ?? (new DateTimeImmutable('now', new DateTimeZone(TZ)))->format(DATE_ATOM);
    $expires   = to_iso($str('expiresAt'));
    if ($published === false || $expires === false) $errors[] = 'Datum ungültig.';
    elseif ($expires !== null && strtotime($expires) <= strtotime($published))
        $errors[] = 'Das Ablaufdatum liegt vor der Veröffentlichung.';

    // The id is fixed once created: the app remembers seen ids, a new id would show the message again.
    if ($editingId !== null) {
        $id = $editingId;
    } else {
        $id = $str('id');
        if ($id === '') $id = date('Y-m-d') . '-' . slug($title);
        if (!preg_match('/^[a-z0-9][a-z0-9-]{0,63}$/', $id)) $errors[] = 'Die ID darf nur a–z, 0–9 und Bindestriche enthalten.';
        elseif (in_array($id, $existingIds, true))         $errors[] = 'Diese ID gibt es schon.';
    }

    if ($errors) return [null, $errors];

    return [array_filter([
        'id'          => $id,
        'title'       => $title,
        'body'        => $body,
        'kind'        => $kind,
        'link'        => $link ?: null,
        'linkLabel'   => $label ?: null,
        'publishedAt' => $published,
        'expiresAt'   => $expires,
        'minVersion'  => $min ?: null,
        'maxVersion'  => $max ?: null,
    ], fn($v) => $v !== null), []];
}
