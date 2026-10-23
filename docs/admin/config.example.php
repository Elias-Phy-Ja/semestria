<?php
// Copy this file to config.php on the server and fill in SETUP_TOKEN.
// config.php is in .gitignore and must never be committed.

// A long random string only you know, e.g. 40 characters from a password manager.
// It is needed exactly once, to set the admin password on the first visit to /admin/.
// After that the setup form is gone; you may then empty this value.
const SETUP_TOKEN = '';

// Where the app reads its messages from. Relative to this folder.
const NEWS_FILE = __DIR__ . '/../news.json';

// Password hash and login attempts. The folder is closed off by its own .htaccess.
const DATA_DIR = __DIR__ . '/data';
