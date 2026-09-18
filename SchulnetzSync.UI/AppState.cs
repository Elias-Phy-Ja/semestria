using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Model;
using SchulnetzSync.UI.Model;

namespace SchulnetzSync.UI;

// ── An entry the user made by hand. Local only, never pushed to Outlook. ─────
public sealed record ManualEventData(
    Guid Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string TypeKey);   // "Pruefung" | "Termin"

/// <summary>
/// Global state — the single source of truth for the config, the cached feed, the tasks,
/// the colours and the comments. Every page reads from here and subscribes to
/// <see cref="Changed"/> rather than talking to the other pages.
///
/// Everything is written to %LOCALAPPDATA%\Semestria as it changes, so a crash costs at
/// most the keystroke that was in flight.
/// </summary>
public static class AppState
{
    private static SyncConfig _config = ConfigManager.Load();

    public static SyncConfig Config
    {
        get => _config;
        set { _config = value; Changed?.Invoke(); }
    }

    /// <summary>Fires whenever the config or a sync result changes.</summary>
    public static event Action? Changed;

    /// <summary>Current status line; empty means nothing is running.</summary>
    public static string SyncStatus { get; set; } = string.Empty;

    /// <summary>True while a sync is running.</summary>
    public static bool IsSyncing { get; set; }

    /// <summary>
    /// True while the feed is being pulled in the background, i.e. the auto-refresh at
    /// start. Kept apart from <see cref="IsSyncing"/> so the dashboard can show both.
    /// </summary>
    public static bool IsRefreshingFeed { get; set; }

    /// <summary>
    /// Notes that the feed came in successfully and stores the timestamp.
    /// Raises <see cref="Changed"/>.
    /// </summary>
    public static void MarkFeedRefreshed(DateTimeOffset when)
    {
        _config.LastFeedRefreshAt = when;
        try { ConfigManager.Save(_config); } catch { /* a timestamp is not worth failing over */ }
        Notify();
    }

    // ── Cached feed, kept on disk ────────────────────────────────────────────
    private static readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "cached-events.json");

    // Enums as strings, otherwise SchulnetzEventType does not survive the round trip
    private static readonly JsonSerializerOptions _eventJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters                  = { new JsonStringEnumConverter() }
    };

    private static IReadOnlyList<SchulnetzEvent> _cachedFeedEvents = LoadCachedEvents();

    /// <summary>
    /// The events from the last parse, filled after every run including a dry one. Written
    /// to disk so the calendar has something to show before the first refresh comes back.
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
        catch { /* corrupt file: start empty rather than not at all */ }
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

    // ── Entries the user hid ─────────────────────────────────────────────────
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

    // ── Colours per category and per subject ─────────────────────────────────
    private static readonly string _colorsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "colors.json");

    private static Dictionary<string, string> _categoryColors = LoadColors();

    public static IReadOnlyDictionary<string, string> CategoryColors => _categoryColors;

    /// <summary>
    /// The hex colour for a key. For a subject code the chain runs: colour set for this
    /// subject, then the global lesson colour, then the built-in default.
    /// </summary>
    public static string GetEventColor(string key)
    {
        if (_categoryColors.TryGetValue(key, out var c)) return c;

        // A subject code rather than one of the fixed categories, so fall back to Lektion.
        if (key != "Pruefung" && key != "Termin" && key != "Lektion" &&
            _categoryColors.TryGetValue("Lektion", out var lektionColor))
            return lektionColor;

        return DefaultColor(key);
    }

    private static string DefaultColor(string key) => key switch
    {
        "Pruefung" => "#DC2626",   // red
        "Termin"   => "#D97706",   // amber
        _          => "#2563EB"    // blue, for lessons and subject codes
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

    // ── Colours for one single entry ─────────────────────────────────────────
    //
    // A colour for exactly one entry, say one TEU lesson or the "Via" appointment.
    // It beats both the subject and the category colour.
    //
    // Keyed by the event key. Exams and appointments keep theirs when they move
    // (P_65100), but a lesson key contains date, time and room — so if that exact
    // lesson gets rescheduled, its individual colour falls back to the subject one.

    private static readonly string _eventColorsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "event-colors.json");

    private static readonly Dictionary<string, string> _eventColors = LoadEventColors();

    /// <summary>
    /// The colour an entry actually gets: individual, then subject or category, then default.
    /// </summary>
    /// <param name="eventKey">Key of the entry itself.</param>
    /// <param name="groupKey">"Pruefung", "Termin", or the subject code.</param>
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

    /// <summary>Drops the individual colour, so the entry goes back to its group colour.</summary>
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

    // ── Hand-made entries ────────────────────────────────────────────────────
    private static readonly string _manualPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "manual-events.json");

    private static List<ManualEventData> _manualEvents = LoadManual();
    public static IReadOnlyList<ManualEventData> ManualEvents => _manualEvents;

    /// <summary>
    /// The hand-made entries as <see cref="SchulnetzEvent"/>, for the calendar, the
    /// dashboard and the sync — one conversion, so all three agree on what they show.
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

    // ── Tasks ────────────────────────────────────────────────────────────────
    private static readonly string _tasksPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "tasks.json");

    private static List<TaskItem> _tasks = LoadTasks();

    /// <summary>Every task, open and done, in the order they were added.</summary>
    public static IReadOnlyList<TaskItem> Tasks => _tasks;

    public static void AddTask(TaskItem task)
    {
        _tasks.Add(task);
        SaveTasks();
        Notify();
    }

    /// <summary>Replaces a task by id. An unknown id is simply ignored.</summary>
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

    /// <summary>Removes finished tasks.</summary>
    /// <param name="scope">
    /// Limits the clean-up to one area, usually a list. Someone tidying up "DEU" does not
    /// expect "ENG" to be emptied as well. Null clears everything.
    /// </param>
    public static int ClearCompletedTasks(Func<TaskItem, bool>? scope = null)
    {
        int removed = _tasks.RemoveAll(t => t.IsDone && (scope is null || scope(t)));
        if (removed > 0) { SaveTasks(); Notify(); }
        return removed;
    }

    // ── Task lists ───────────────────────────────────────────────────────────
    private static readonly string _taskListsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "task-lists.json");

    private static List<string> _taskLists = LoadTaskLists();

    /// <summary>
    /// Every list name: the ones created on purpose plus the ones still attached to a
    /// task. That way a list does not vanish just because nobody ever created it formally.
    /// </summary>
    public static IReadOnlyList<string> TaskLists()
        => _taskLists
            .Concat(_tasks.Select(t => t.ListName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>Creates a list. A name that already exists is ignored.</summary>
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
    /// Removes a list. The tasks in it survive and move to "Ohne Liste" — deleting a
    /// list should never destroy work.
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

    // ── Colours for the task lists ───────────────────────────────────────────
    //
    // Deliberately separate from the calendar colours: a list called "WIR" has no subject
    // behind it, and even a list called "DEU" should be allowed to look different from the
    // DEU lessons.

    private static readonly string _taskListColorsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "task-list-colors.json");

    private static readonly Dictionary<string, string> _taskListColors = LoadTaskListColors();

    /// <summary>
    /// A list colour as hex.
    ///
    /// A list without one gets the least used colour from the palette on first read and
    /// keeps it. Without storing it the colours would shuffle around every time a new list
    /// appeared.
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
    /// Suggestions for new lists: the subject codes from the lessons in the feed, minus
    /// the ones that already have a list.
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
        catch { /* corrupt file: start empty rather than not at all */ }
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

    // ── Subject comments ─────────────────────────────────────────────────────
    private static readonly string _subjectCommentsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "subject-comments.json");

    private static readonly List<SubjectComment> _subjectComments = LoadSubjectComments();

    /// <summary>The notes on one subject, newest first.</summary>
    public static IReadOnlyList<SubjectComment> CommentsFor(string subject)
        => _subjectComments
            .Where(c => string.Equals(c.Subject, subject, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

    public static int CommentCount(string subject)
        => _subjectComments.Count(c => string.Equals(c.Subject, subject, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a note. Empty text is ignored.</summary>
    public static void AddSubjectComment(string subject, string text)
    {
        var clean = CleanComment(text);
        if (clean.Length == 0 || string.IsNullOrWhiteSpace(subject)) return;

        _subjectComments.Add(new SubjectComment(
            Guid.NewGuid(), subject.Trim().ToUpperInvariant(), clean, DateTimeOffset.Now, null));
        SaveSubjectComments();
        Notify();
    }

    /// <summary>Changes the text. Emptying the box does not delete the note, it is ignored.</summary>
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
        catch { /* corrupt file: start empty rather than not at all */ }
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

    // ── Resetting the calendar ───────────────────────────────────────────────

    /// <summary>Clears hidden entries and hand-made events. Colours stay.</summary>
    public static void ClearCalendar()
    {
        _suppressedKeys.Clear(); SaveSuppressed();
        _manualEvents.Clear();   SaveManual();
        Notify();
    }

    /// <summary>Clears the lot: hidden entries, hand-made events and every colour.</summary>
    public static void ResetAll()
    {
        _suppressedKeys.Clear(); SaveSuppressed();
        _manualEvents.Clear();   SaveManual();
        _categoryColors.Clear(); SaveColors();
        _eventColors.Clear();    SaveEventColors();
        Notify();
    }

    // ── Small helpers ────────────────────────────────────────────────────────

    public static void Reload()
    {
        _config = ConfigManager.Load();
        Changed?.Invoke();
    }

    public static void Notify() => Changed?.Invoke();
}
