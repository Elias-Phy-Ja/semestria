using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Model;
using SchulnetzSync.UI.Model;

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

    /// <summary>
    /// Ob gerade der Feed im Hintergrund geladen wird (Auto-Refresh beim Start).
    /// Getrennt von <see cref="IsSyncing"/>, damit das Dashboard beides anzeigen kann.
    /// </summary>
    public static bool IsRefreshingFeed { get; set; }

    /// <summary>
    /// Hält fest, dass der Feed soeben erfolgreich geladen wurde, und persistiert
    /// den Zeitstempel. Löst <see cref="Changed"/> aus.
    /// </summary>
    public static void MarkFeedRefreshed(DateTimeOffset when)
    {
        _config.LastFeedRefreshAt = when;
        try { ConfigManager.Save(_config); } catch { /* Zeitstempel ist nicht kritisch */ }
        Notify();
    }

    // ── Persistierter Feed-Cache ─────────────────────────────────────────────
    private static readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "cached-events.json");

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
        "Semestria", "suppressed.json");

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
        "Semestria", "colors.json");

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
        _eventColors.Clear();
        SaveEventColors();
        Notify();
    }

    // ── Einzelfarben ─────────────────────────────────────────────────────────
    //
    // Eine Farbe für genau einen Eintrag, etwa eine einzelne TEU-Lektion oder
    // den Termin «Via». Sie sticht die Fach- und Kategoriefarbe.
    //
    // Schlüssel ist der Event-Key. Prüfungen und Termine behalten ihn auch
    // beim Verschieben (P_65100). Bei Lektionen enthält er Datum, Zeit und
    // Raum — wird genau diese Lektion verlegt, fällt die Einzelfarbe auf die
    // Fachfarbe zurück.

    private static readonly string _eventColorsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "event-colors.json");

    private static readonly Dictionary<string, string> _eventColors = LoadEventColors();

    /// <summary>
    /// Effektive Farbe eines Eintrags: Einzelfarbe → Fach/Kategorie → Standard.
    /// </summary>
    /// <param name="eventKey">Der Event-Key des Eintrags.</param>
    /// <param name="groupKey">"Pruefung", "Termin" oder das Fachkürzel.</param>
    public static string GetEventColor(string eventKey, string groupKey)
        => _eventColors.TryGetValue(eventKey, out var hex) ? hex : GetEventColor(groupKey);

    /// <summary>True when this single entry has its own colour.</summary>
    public static bool HasOwnEventColor(string eventKey) => _eventColors.ContainsKey(eventKey);

    public static void SetOwnEventColor(string eventKey, string hex)
    {
        _eventColors[eventKey] = hex;
        SaveEventColors();
        Notify();
    }

    /// <summary>Entfernt die Einzelfarbe; der Eintrag zeigt wieder die Fach- bzw. Kategoriefarbe.</summary>
    public static void ClearOwnEventColor(string eventKey)
    {
        if (!_eventColors.Remove(eventKey)) return;
        SaveEventColors();
        Notify();
    }

    private static Dictionary<string, string> LoadEventColors()
    {
        try
        {
            if (File.Exists(_eventColorsPath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(_eventColorsPath)) ?? [];
        }
        catch { }
        return [];
    }

    private static void SaveEventColors()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_eventColorsPath)!);
            File.WriteAllText(_eventColorsPath, JsonSerializer.Serialize(_eventColors));
        }
        catch { }
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
        "Semestria", "manual-events.json");

    private static List<ManualEventData> _manualEvents = LoadManual();
    public static IReadOnlyList<ManualEventData> ManualEvents => _manualEvents;

    /// <summary>
    /// Die manuellen Einträge als <see cref="SchulnetzEvent"/> — für Kalender,
    /// Dashboard und Sync, damit alle drei dieselbe Umwandlung verwenden.
    /// </summary>
    public static IEnumerable<SchulnetzEvent> ManualAsEvents()
        => _manualEvents.Select(m => new SchulnetzEvent(
            Key:      EventKeys.ForManual(m.Id),
            RawUid:   EventKeys.ForManual(m.Id),
            Type:     m.TypeKey == "Pruefung" ? SchulnetzEventType.Pruefung
                                              : SchulnetzEventType.Termin,
            Start:    m.Start,
            End:      m.End,
            IsAllDay: m.IsAllDay,
            Summary:  m.Title,
            Location: m.Location));

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

    // ── Aufgaben ─────────────────────────────────────────────────────────────
    private static readonly string _tasksPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "tasks.json");

    private static List<TaskItem> _tasks = LoadTasks();

    /// <summary>Alle Aufgaben, offene wie erledigte, in Eingabereihenfolge.</summary>
    public static IReadOnlyList<TaskItem> Tasks => _tasks;

    public static void AddTask(TaskItem task)
    {
        _tasks.Add(task);
        SaveTasks();
        Notify();
    }

    /// <summary>Ersetzt eine Aufgabe anhand ihrer Id. Unbekannte Ids werden ignoriert.</summary>
    public static void UpdateTask(TaskItem task)
    {
        var i = _tasks.FindIndex(t => t.Id == task.Id);
        if (i < 0) return;
        _tasks[i] = task;
        SaveTasks();
        Notify();
    }

    public static void RemoveTask(Guid id)
    {
        _tasks.RemoveAll(t => t.Id == id);
        SaveTasks();
        Notify();
    }

    /// <summary>
    /// Entfernt erledigte Aufgaben.
    /// </summary>
    /// <param name="scope">
    /// Schränkt auf einen Bereich ein, etwa eine Liste. Wer in «DEU» aufräumt,
    /// erwartet nicht, dass auch «ENG» geleert wird. Null = alle.
    /// </param>
    public static int ClearCompletedTasks(Func<TaskItem, bool>? scope = null)
    {
        int removed = _tasks.RemoveAll(t => t.IsDone && (scope is null || scope(t)));
        if (removed > 0) { SaveTasks(); Notify(); }
        return removed;
    }

    // ── Aufgabenlisten ───────────────────────────────────────────────────────
    private static readonly string _taskListsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "task-lists.json");

    private static List<string> _taskLists = LoadTaskLists();

    /// <summary>
    /// Alle Listennamen: ausdrücklich angelegte plus solche, die noch an einer
    /// Aufgabe hängen. So verschwindet eine Liste nicht, nur weil sie nie
    /// separat angelegt wurde.
    /// </summary>
    public static IReadOnlyList<string> TaskLists()
        => _taskLists
            .Concat(_tasks.Select(t => t.ListName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>Legt eine Liste an. Doppelte Namen werden ignoriert.</summary>
    public static void AddTaskList(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        if (TaskLists().Contains(name, StringComparer.OrdinalIgnoreCase)) return;

        _taskLists.Add(name);
        SaveTaskLists();
        Notify();
    }

    /// <summary>
    /// Entfernt eine Liste. Die Aufgaben darin bleiben erhalten und rutschen
    /// nach «Ohne Liste» — Löschen der Liste soll keine Arbeit vernichten.
    /// </summary>
    public static void RemoveTaskList(string name)
    {
        _taskLists.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < _tasks.Count; i++)
            if (string.Equals(_tasks[i].ListName, name, StringComparison.OrdinalIgnoreCase))
                _tasks[i] = _tasks[i] with { ListName = "" };

        _taskListColors.Remove(name);

        SaveTaskLists();
        SaveTaskListColors();
        SaveTasks();
        Notify();
    }

    // ── Listenfarben ─────────────────────────────────────────────────────────
    //
    // Bewusst getrennt von den Kalenderfarben: Eine Liste «WIR» hat kein Fach,
    // und auch eine Liste «DEU» soll anders aussehen dürfen als die Lektionen.

    private static readonly string _taskListColorsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "task-list-colors.json");

    private static readonly Dictionary<string, string> _taskListColors = LoadTaskListColors();

    /// <summary>
    /// Farbe einer Liste als Hex-Wert.
    ///
    /// Hat eine Liste noch keine, bekommt sie beim ersten Abruf die am wenigsten
    /// genutzte Palettenfarbe und behält sie. Ohne Speichern würden sich die
    /// Farben verschieben, sobald eine Liste dazukommt.
    /// </summary>
    public static string TaskListColor(string name)
    {
        if (_taskListColors.TryGetValue(name, out var hex)) return hex;

        hex = ColorPalette.NextAutoColor(_taskListColors.Values);
        _taskListColors[name] = hex;
        SaveTaskListColors();
        return hex;
    }

    public static void SetTaskListColor(string name, string hex)
    {
        _taskListColors[name] = hex;
        SaveTaskListColors();
        Notify();
    }

    private static Dictionary<string, string> LoadTaskListColors()
    {
        try
        {
            if (File.Exists(_taskListColorsPath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(_taskListColorsPath));
                if (loaded is not null)
                    return new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveTaskListColors()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_taskListColorsPath)!);
            File.WriteAllText(_taskListColorsPath, JsonSerializer.Serialize(_taskListColors));
        }
        catch { }
    }

    /// <summary>
    /// Vorschläge für neue Listen: die Fachkürzel aus den Lektionen des Feeds,
    /// soweit es dafür noch keine Liste gibt.
    /// </summary>
    public static IReadOnlyList<string> SuggestedListNames()
    {
        var existing = TaskLists();

        return _cachedFeedEvents
            .Where(e => e.Type == SchulnetzEventType.Lektion)
            .Select(e => SubjectCode.FromSummary(e.Summary))
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(c => !existing.Contains(c, StringComparer.OrdinalIgnoreCase))
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();
    }

    private static List<string> LoadTaskLists()
    {
        try
        {
            if (File.Exists(_taskListsPath))
                return JsonSerializer.Deserialize<List<string>>(
                    File.ReadAllText(_taskListsPath)) ?? [];
        }
        catch { }
        return [];
    }

    private static void SaveTaskLists()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_taskListsPath)!);
            File.WriteAllText(_taskListsPath, JsonSerializer.Serialize(_taskLists));
        }
        catch { }
    }

    private static List<TaskItem> LoadTasks()
    {
        try
        {
            if (File.Exists(_tasksPath))
                return JsonSerializer.Deserialize<List<TaskItem>>(
                    File.ReadAllText(_tasksPath)) ?? [];
        }
        catch { /* bei korrupten Daten leer starten */ }
        return [];
    }

    private static void SaveTasks()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_tasksPath)!);
            File.WriteAllText(_tasksPath, JsonSerializer.Serialize(_tasks));
        }
        catch { }
    }

    // ── Fachkommentare ───────────────────────────────────────────────────────
    private static readonly string _subjectCommentsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "subject-comments.json");

    private static readonly List<SubjectComment> _subjectComments = LoadSubjectComments();

    /// <summary>Kommentare zu einem Fach, neueste zuerst.</summary>
    public static IReadOnlyList<SubjectComment> CommentsFor(string subject)
        => _subjectComments
            .Where(c => string.Equals(c.Subject, subject, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

    public static int CommentCount(string subject)
        => _subjectComments.Count(c => string.Equals(c.Subject, subject, StringComparison.OrdinalIgnoreCase));

    /// <summary>Legt einen Kommentar an. Leerer Text wird ignoriert.</summary>
    public static void AddSubjectComment(string subject, string text)
    {
        var clean = CleanComment(text);
        if (clean.Length == 0 || string.IsNullOrWhiteSpace(subject)) return;

        _subjectComments.Add(new SubjectComment(
            Guid.NewGuid(), subject.Trim().ToUpperInvariant(), clean, DateTimeOffset.Now, null));
        SaveSubjectComments();
        Notify();
    }

    /// <summary>Ändert den Text. Leerer Text löscht den Kommentar nicht, sondern wird ignoriert.</summary>
    public static void UpdateSubjectComment(Guid id, string text)
    {
        var clean = CleanComment(text);
        var i     = _subjectComments.FindIndex(c => c.Id == id);
        if (i < 0 || clean.Length == 0 || clean == _subjectComments[i].Text) return;

        _subjectComments[i] = _subjectComments[i] with { Text = clean, EditedAt = DateTimeOffset.Now };
        SaveSubjectComments();
        Notify();
    }

    public static void RemoveSubjectComment(Guid id)
    {
        if (_subjectComments.RemoveAll(c => c.Id == id) == 0) return;
        SaveSubjectComments();
        Notify();
    }

    private static string CleanComment(string text)
    {
        var clean = text.Trim();
        return clean.Length > SubjectComment.MaxLength ? clean[..SubjectComment.MaxLength] : clean;
    }

    private static List<SubjectComment> LoadSubjectComments()
    {
        try
        {
            if (File.Exists(_subjectCommentsPath))
                return JsonSerializer.Deserialize<List<SubjectComment>>(
                    File.ReadAllText(_subjectCommentsPath)) ?? [];
        }
        catch { /* bei korrupten Daten leer starten */ }
        return [];
    }

    private static void SaveSubjectComments()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_subjectCommentsPath)!);
            File.WriteAllText(_subjectCommentsPath, JsonSerializer.Serialize(_subjectComments));
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
        _eventColors.Clear();    SaveEventColors();
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
