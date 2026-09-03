using System.IO;
using System.Text.Json;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Model;

namespace SchulnetzSync.UI;

/// <summary>
/// Globaler App-Zustand — einzige Quelle der Wahrheit für Config und letzte Sync-Ergebnisse.
/// Alle Seiten lesen von hier und abonnieren <see cref="Changed"/> für Aktualisierungen.
/// </summary>
public static class AppState
{
    private static SyncConfig _config = ConfigManager.Load();

    public static SyncConfig Config
    {
        get => _config;
        set { _config = value; Changed?.Invoke(); }
    }

    /// <summary>Wird ausgelöst wenn sich Config oder ein Sync-Ergebnis ändert.</summary>
    public static event Action? Changed;

    /// <summary>Aktuelle Sync-Statusmeldung (leer = kein laufender Sync).</summary>
    public static string SyncStatus { get; set; } = string.Empty;

    /// <summary>Ob gerade ein Sync läuft.</summary>
    public static bool IsSyncing { get; set; }

    /// <summary>
    /// Zuletzt geparste Feed-Events — wird nach jedem Sync-Lauf (inkl. Dry-Run) befüllt.
    /// Leer wenn noch kein Sync gemacht wurde oder der letzte Sync fehlschlug.
    /// </summary>
    public static IReadOnlyList<SchulnetzEvent> CachedFeedEvents { get; set; }
        = Array.Empty<SchulnetzEvent>();

    // ── Ausgeblendete Events ────────────────────────────────────────────────
    private static readonly string _suppressedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchulnetzSync", "suppressed.json");

    private static HashSet<string> _suppressedKeys = LoadSuppressed();

    /// <summary>Correlation-Keys (z.B. «P_65100»), die im In-App-Kalender ausgeblendet sind.</summary>
    public static IReadOnlySet<string> SuppressedKeys => _suppressedKeys;

    /// <summary>Blendet einen Event aus und speichert die Liste.</summary>
    public static void SuppressEvent(string key)
    {
        _suppressedKeys.Add(key);
        SaveSuppressed();
        Notify();
    }

    /// <summary>Macht einen ausgeblendeten Event wieder sichtbar.</summary>
    public static void UnsuppressEvent(string key)
    {
        _suppressedKeys.Remove(key);
        SaveSuppressed();
        Notify();
    }

    private static HashSet<string> LoadSuppressed()
    {
        try
        {
            if (File.Exists(_suppressedPath))
                return JsonSerializer.Deserialize<HashSet<string>>(
                    File.ReadAllText(_suppressedPath)) ?? [];
        }
        catch { /* Fehler beim Lesen ignorieren — leer starten */ }
        return [];
    }

    private static void SaveSuppressed()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_suppressedPath)!);
            File.WriteAllText(_suppressedPath,
                JsonSerializer.Serialize(_suppressedKeys));
        }
        catch { /* Fehler beim Schreiben ignorieren */ }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Lädt Config neu und benachrichtigt alle Abonnenten.</summary>
    public static void Reload()
    {
        _config = ConfigManager.Load();
        Changed?.Invoke();
    }

    public static void Notify() => Changed?.Invoke();
}
