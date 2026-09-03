using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Model;

namespace SchulnetzSync.UI;

// ── Manuell erstellter Termin (nur lokal, nie in Outlook) ────────────────────
public sealed record ManualEventData(
    Guid Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string TypeKey);   // "Pruefung" | "Termin"

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

    // ── Persistierter Feed-Cache ─────────────────────────────────────────────
    private static readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchulnetzSync", "cached-events.json");

    // Enums als Strings serialisieren damit SchulnetzEventType korrekt rund-reist
    private static readonly JsonSerializerOptions _eventJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters                  = { new JsonStringEnumConverter() }
    };

    private static IReadOnlyList<SchulnetzEvent> _cachedFeedEvents = LoadCachedEvents();

    /// <summary>
    /// Zuletzt geparste Feed-Events — wird nach jedem Sync-Lauf (inkl. Dry-Run) befüllt.
    /// Wird auf Disk persistiert und beim nächsten Start automatisch geladen.
    /// </summary>
    public static IReadOnlyList<SchulnetzEvent> CachedFeedEvents
    {
        get => _cachedFeedEvents;
        set
        {
            _cachedFeedEvents = value;
            SaveCachedEvents();
        }
    }

    private static IReadOnlyList<SchulnetzEvent> LoadCachedEvents()
    {
        try
        {
            if (File.Exists(_cachePath))
                return JsonSerializer.Deserialize<List<SchulnetzEvent>>(
                    File.ReadAllText(_cachePath), _eventJsonOpts)
                    ?? [];
        }
        catch { /* bei korrupten Daten leer starten */ }
        return [];
    }

    private static void SaveCachedEvents()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath,
                JsonSerializer.Serialize(_cachedFeedEvents, _eventJsonOpts));
        }
        catch { }
    }

    // ── Ausgeblendete Events ─────────────────────────────────────────────────
    private static readonly string _suppressedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchulnetzSync", "suppressed.json");

    private static HashSet<string> _suppressedKeys = LoadSuppressed();

    public static IReadOnlySet<string> SuppressedKeys => _suppressedKeys;

    public static void SuppressEvent(string key)   { _suppressedKeys.Add(key);    SaveSuppressed(); Notify(); }
    public static void UnsuppressEvent(string key) { _suppressedKeys.Remove(key); SaveSuppressed(); Notify(); }
    public static void ClearSuppressed()           { _suppressedKeys.Clear();      SaveSuppressed(); Notify(); }

    private static HashSet<string> LoadSuppressed()
    {
        try
        {
            if (File.Exists(_suppressedPath))
                return JsonSerializer.Deserialize<HashSet<string>>(
                    File.ReadAllText(_suppressedPath)) ?? [];
        }
        catch { }
        return [];
    }

    private static void SaveSuppressed()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_suppressedPath)!);
            File.WriteAllText(_suppressedPath, JsonSerializer.Serialize(_suppressedKeys));
        }
        catch { }
    }

    // ── Kategoriefarben ──────────────────────────────────────────────────────
    private static readonly string _colorsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchulnetzSync", "colors.json");

    private static Dictionary<string, string> _categoryColors = LoadColors();

    public static IReadOnlyDictionary<string, string> CategoryColors => _categoryColors;

    /// <summary>
    /// Gibt die Hex-Farbe für einen Schlüssel zurück.
    /// Fallback-Kette für Fachkürzel: spezifisch → globale Lektion-Farbe → Standardfarbe.
    /// </summary>
    public static string GetEventColor(string key)
    {
        if (_categoryColors.TryGetValue(key, out var c)) return c;

        // Fachkürzel (kein fixes Kategorie-Schlüssel) → globale Lektion-Farbe verwenden
        if (key != "Pruefung" && key != "Termin" && key != "Lektion" &&
            _categoryColors.TryGetValue("Lektion", out var lektionColor))
            return lektionColor;

        return DefaultColor(key);
    }

    private static string DefaultColor(string key) => key switch
    {
        "Pruefung" => "#DC2626",   // Rot
        "Termin"   => "#D97706",   // Amber/Gelb
        _          => "#2563EB"    // Blau (Lektionen + Fachkürzel)
    };

    public static void SetCategoryColor(string key, string hex)
    {
        _categoryColors[key] = hex;
        SaveColors();
        Notify();
    }

    public static void ResetCategoryColors()
    {
        _categoryColors.Clear();
        SaveColors();
        Notify();
    }

    private static Dictionary<string, string> LoadColors()
    {
        try
        {
            if (File.Exists(_colorsPath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(_colorsPath)) ?? [];
        }
        catch { }
        return [];
    }

    private static void SaveColors()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_colorsPath)!);
            File.WriteAllText(_colorsPath, JsonSerializer.Serialize(_categoryColors));
        }
        catch { }
    }

    // ── Manuelle Events ──────────────────────────────────────────────────────
    private static readonly string _manualPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchulnetzSync", "manual-events.json");

    private static List<ManualEventData> _manualEvents = LoadManual();
    public static IReadOnlyList<ManualEventData> ManualEvents => _manualEvents;

    public static void AddManualEvent(ManualEventData ev)
    {
        _manualEvents.Add(ev);
        SaveManual();
        Notify();
    }

    public static void RemoveManualEvent(Guid id)
    {
        _manualEvents.RemoveAll(e => e.Id == id);
        SaveManual();
        Notify();
    }

    public static void ClearManualEvents()
    {
        _manualEvents.Clear();
        SaveManual();
        Notify();
    }

    private static List<ManualEventData> LoadManual()
    {
        try
        {
            if (File.Exists(_manualPath))
                return JsonSerializer.Deserialize<List<ManualEventData>>(
                    File.ReadAllText(_manualPath)) ?? [];
        }
        catch { }
        return [];
    }

    private static void SaveManual()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_manualPath)!);
            File.WriteAllText(_manualPath, JsonSerializer.Serialize(_manualEvents));
        }
        catch { }
    }

    // ── Kalender-Reset-Helpers ───────────────────────────────────────────────

    /// <summary>Löscht ausgeblendete Einträge und manuelle Events (Farben bleiben).</summary>
    public static void ClearCalendar()
    {
        _suppressedKeys.Clear(); SaveSuppressed();
        _manualEvents.Clear();   SaveManual();
        Notify();
    }

    /// <summary>Setzt alles zurück: ausgeblendete Einträge, manuelle Events und Farben.</summary>
    public static void ResetAll()
    {
        _suppressedKeys.Clear(); SaveSuppressed();
        _manualEvents.Clear();   SaveManual();
        _categoryColors.Clear(); SaveColors();
        Notify();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public static void Reload()
    {
        _config = ConfigManager.Load();
        Changed?.Invoke();
    }

    public static void Notify() => Changed?.Invoke();
}
