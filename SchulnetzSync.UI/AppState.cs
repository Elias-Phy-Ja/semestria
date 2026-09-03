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

    /// <summary>Lädt Config neu und benachrichtigt alle Abonnenten.</summary>
    public static void Reload()
    {
        _config = ConfigManager.Load();
        Changed?.Invoke();
    }

    public static void Notify() => Changed?.Invoke();
}
