<?php
// News admin for Semestria: one page for setup, login, the list and the edit form.
// Every change is a POST with a CSRF token, followed by a redirect (post/redirect/get).

declare(strict_types=1);
require __DIR__ . '/lib.php';

send_security_headers();
start_session();

$hasPassword = stored_password_hash() !== null;
$method      = $_SERVER['REQUEST_METHOD'] ?? 'GET';
$action      = is_string($_POST['action'] ?? null) ? $_POST['action'] : '';
$formErrors  = [];
$formValues  = null;

if ($method === 'POST') {
    require_csrf();

    // ── First visit: set the password, only with the setup token ─────────
    if ($action === 'setup' && !$hasPassword) {
        $token = (string)($_POST['token'] ?? '');
        if (SETUP_TOKEN === '' || !hash_equals(SETUP_TOKEN, $token)) {
            record_failed_login();
            sleep(1);
            flash('Setup-Token falsch.', true);
            redirect_self();
        }
        $pw = (string)($_POST['password'] ?? '');
        if ($problem = password_problem($pw, (string)($_POST['repeat'] ?? ''))) {
            flash($problem, true);
            redirect_self();
        }
        store_password_hash($pw);
        session_regenerate_id(true);
        $_SESSION['admin'] = true;
        flash('Passwort gesetzt. Du kannst SETUP_TOKEN in config.php jetzt leeren.');
        redirect_self();
    }

    // ── Login ────────────────────────────────────────────────────────────
    if ($action === 'login' && $hasPassword) {
        if (($wait = lockout_remaining()) > 0) {
            flash('Zu viele Fehlversuche. Versuch es in ' . ceil($wait / 60) . ' Minuten wieder.', true);
            redirect_self();
        }
        $hash = stored_password_hash();
        if (!password_verify((string)($_POST['password'] ?? ''), $hash)) {
            record_failed_login();
            sleep(1);
            flash('Passwort falsch.', true);
            redirect_self();
        }
        if (password_needs_rehash($hash, PASSWORD_DEFAULT)) {
            store_password_hash((string)$_POST['password']);
        }
        clear_failed_logins();
        session_regenerate_id(true);
        $_SESSION['admin'] = true;
        redirect_self();
    }

    if (!is_logged_in()) {
        redirect_self();
    }

    // ── Logged in from here on ───────────────────────────────────────────
    $items = load_news();
    $ids   = array_column($items, 'id');

    switch ($action) {
        case 'logout':
            $_SESSION = [];
            session_regenerate_id(true);
            redirect_self();

        case 'save':
            $editingId = is_string($_POST['editing'] ?? null) && $_POST['editing'] !== '' ? $_POST['editing'] : null;
            if ($editingId !== null && !in_array($editingId, $ids, true)) {
                flash('Diese Nachricht gibt es nicht mehr.', true);
                redirect_self();
            }
            [$item, $formErrors] = validate_item($_POST, $editingId, $ids);
            if ($item === null) {
                $formValues = $_POST;   // show the form again with what was typed
                break;
            }
            $items = array_values(array_filter($items, fn($i) => ($i['id'] ?? '') !== $item['id']));
            $items[] = $item;
            save_news($items);
            flash($editingId === null ? 'Nachricht veröffentlicht.' : 'Nachricht gespeichert.');
            redirect_self();

        case 'delete':
            $id = (string)($_POST['id'] ?? '');
            save_news(array_values(array_filter($items, fn($i) => ($i['id'] ?? '') !== $id)));
            flash('Nachricht gelöscht.');
            redirect_self();

        case 'password':
            if (!password_verify((string)($_POST['current'] ?? ''), stored_password_hash())) {
                flash('Aktuelles Passwort falsch.', true);
                redirect_self('pw');
            }
            $pw = (string)($_POST['password'] ?? '');
            if ($problem = password_problem($pw, (string)($_POST['repeat'] ?? ''))) {
                flash($problem, true);
                redirect_self('pw');
            }
            store_password_hash($pw);
            session_regenerate_id(true);
            flash('Passwort geändert.');
            redirect_self();

        default:
            redirect_self();
    }
}

// ── Rendering ────────────────────────────────────────────────────────────

$flash   = take_flash();
$csrf    = csrf_token();
$items   = is_logged_in() ? load_news() : [];
$editId  = is_string($_GET['edit'] ?? null) ? $_GET['edit'] : null;
$editing = null;
foreach ($items as $i) if (($i['id'] ?? null) === $editId) $editing = $i;
if ($formValues !== null && isset($_POST['editing']) && $_POST['editing'] !== '') {
    $editId = (string)$_POST['editing'];
}

// The values shown in the form: what was just typed, the message being edited, or defaults
$v = $formValues ?? ($editing === null ? ['kind' => 'info'] : array_merge($editing, [
    'publishedAt' => to_local($editing['publishedAt'] ?? null),
    'expiresAt'   => to_local($editing['expiresAt'] ?? null),
]));
$val = fn(string $k) => h(is_string($v[$k] ?? null) ? $v[$k] : '');

function status_of(array $i): string
{
    $now = time();
    if (strtotime($i['publishedAt'] ?? '') > $now) return 'geplant';
    if (!empty($i['expiresAt']) && strtotime($i['expiresAt']) <= $now) return 'abgelaufen';
    return 'aktiv';
}
?>
<!DOCTYPE html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title>Semestria · Nachrichten</title>
<link rel="stylesheet" href="admin.css">
<link rel="icon" href="../favicon.ico">
</head>
<body>
<main>
  <header>
    <h1>Semestria · Nachrichten</h1>
    <?php if (is_logged_in()): ?>
      <form method="post" class="inline">
        <input type="hidden" name="csrf" value="<?= h($csrf) ?>">
        <a class="link" href="?pw">Passwort ändern</a>
        <button name="action" value="logout" class="ghost">Abmelden</button>
      </form>
    <?php endif; ?>
  </header>

  <?php if ($flash): ?>
    <p class="flash <?= $flash['error'] ? 'error' : '' ?>"><?= h($flash['text']) ?></p>
  <?php endif; ?>

<?php if (!$hasPassword): ?>
  <section class="card narrow">
    <h2>Einrichten</h2>
    <p class="hint">Einmalig: Setup-Token aus <code>config.php</code> eingeben und ein Admin-Passwort festlegen (mindestens 14 Zeichen).</p>
    <?php if (SETUP_TOKEN === ''): ?>
      <p class="flash error">In config.php ist kein SETUP_TOKEN eingetragen.</p>
    <?php else: ?>
    <form method="post" autocomplete="off">
      <input type="hidden" name="csrf" value="<?= h($csrf) ?>">
      <label>Setup-Token <input type="password" name="token" required></label>
      <label>Neues Passwort <input type="password" name="password" minlength="14" required autocomplete="new-password"></label>
      <label>Passwort wiederholen <input type="password" name="repeat" minlength="14" required autocomplete="new-password"></label>
      <button name="action" value="setup">Passwort festlegen</button>
    </form>
    <?php endif; ?>
  </section>

<?php elseif (!is_logged_in()): ?>
  <section class="card narrow">
    <h2>Anmelden</h2>
    <form method="post">
      <input type="hidden" name="csrf" value="<?= h($csrf) ?>">
      <label>Passwort <input type="password" name="password" required autofocus autocomplete="current-password"></label>
      <button name="action" value="login">Anmelden</button>
    </form>
  </section>

<?php elseif (isset($_GET['pw'])): ?>
  <section class="card narrow">
    <h2>Passwort ändern</h2>
    <form method="post">
      <input type="hidden" name="csrf" value="<?= h($csrf) ?>">
      <label>Aktuelles Passwort <input type="password" name="current" required autocomplete="current-password"></label>
      <label>Neues Passwort <input type="password" name="password" minlength="14" required autocomplete="new-password"></label>
      <label>Wiederholen <input type="password" name="repeat" minlength="14" required autocomplete="new-password"></label>
      <button name="action" value="password">Ändern</button>
      <a class="link" href="./">Abbrechen</a>
    </form>
  </section>

<?php else: ?>
  <div class="grid">
    <section class="card">
      <h2><?= $editId !== null ? 'Nachricht bearbeiten' : 'Neue Nachricht' ?></h2>
      <?php foreach ($formErrors as $e): ?><p class="flash error"><?= h($e) ?></p><?php endforeach; ?>
      <form method="post">
        <input type="hidden" name="csrf" value="<?= h($csrf) ?>">
        <input type="hidden" name="editing" value="<?= h($editId ?? '') ?>">

        <label>Titel <input name="title" maxlength="120" required value="<?= $val('title') ?>"></label>
        <label>Text <textarea name="body" rows="6" maxlength="2000"><?= $val('body') ?></textarea></label>

        <div class="row">
          <label>Art
            <select name="kind">
              <?php foreach (KINDS as $k => $label): ?>
                <option value="<?= h($k) ?>" <?= ($v['kind'] ?? '') === $k ? 'selected' : '' ?>><?= h($label) ?></option>
              <?php endforeach; ?>
            </select>
          </label>
          <label>ID <?= $editId !== null ? '(fest)' : '(leer = automatisch)' ?>
            <input name="id" pattern="[a-z0-9][a-z0-9\-]{0,63}" value="<?= h($editId ?? (is_string($v['id'] ?? null) ? $v['id'] : '')) ?>" <?= $editId !== null ? 'disabled' : '' ?>>
          </label>
        </div>

        <div class="row">
          <label>Link (https, z. B. Umfrage) <input name="link" type="url" maxlength="500" placeholder="https://forms.office.com/…" value="<?= $val('link') ?>"></label>
          <label>Button-Text <input name="linkLabel" maxlength="40" placeholder="Zur Umfrage" value="<?= $val('linkLabel') ?>"></label>
        </div>

        <div class="row">
          <label>Sichtbar ab <input name="publishedAt" type="datetime-local" value="<?= $val('publishedAt') ?>"></label>
          <label>Sichtbar bis (optional) <input name="expiresAt" type="datetime-local" value="<?= $val('expiresAt') ?>"></label>
        </div>

        <div class="row">
          <label>Ab App-Version (optional) <input name="minVersion" placeholder="2.4.0" value="<?= $val('minVersion') ?>"></label>
          <label>Bis App-Version (optional) <input name="maxVersion" placeholder="2.4.0" value="<?= $val('maxVersion') ?>"></label>
        </div>

        <p class="hint">Leeres «Sichtbar ab» heisst sofort. Zeiten in Schweizer Zeit. Die App zeigt jede Nachricht nur einmal.</p>
        <button name="action" value="save"><?= $editId !== null ? 'Speichern' : 'Veröffentlichen' ?></button>
        <?php if ($editId !== null): ?><a class="link" href="./">Abbrechen</a><?php endif; ?>
      </form>
    </section>

    <section>
      <h2>Alle Nachrichten (<?= count($items) ?>)</h2>
      <?php if (!$items): ?>
        <p class="hint">Noch keine. Die App zeigt nichts an, solange die Liste leer ist.</p>
      <?php endif; ?>
      <?php foreach ($items as $i): $st = status_of($i); ?>
        <article class="card item <?= h($st) ?>">
          <div class="meta">
            <span class="badge <?= h($st) ?>"><?= h($st) ?></span>
            <span><?= h(KINDS[$i['kind'] ?? 'info'] ?? 'Info') ?></span>
            <span><?= h(to_local($i['publishedAt'] ?? null)) ?></span>
            <code><?= h($i['id'] ?? '') ?></code>
          </div>
          <h3><?= h($i['title'] ?? '') ?></h3>
          <?php if (!empty($i['body'])): ?><p><?= nl2br(h($i['body'])) ?></p><?php endif; ?>
          <?php if (!empty($i['link'])): ?><p class="hint">↗ <?= h($i['linkLabel'] ?? 'Link') ?>: <?= h($i['link']) ?></p><?php endif; ?>
          <form method="post" class="inline">
            <input type="hidden" name="csrf" value="<?= h($csrf) ?>">
            <input type="hidden" name="id" value="<?= h($i['id'] ?? '') ?>">
            <a class="link" href="?edit=<?= h(rawurlencode($i['id'] ?? '')) ?>">Bearbeiten</a>
            <button name="action" value="delete" class="ghost danger">Löschen</button>
          </form>
        </article>
      <?php endforeach; ?>
    </section>
  </div>
<?php endif; ?>
</main>
</body>
</html>
