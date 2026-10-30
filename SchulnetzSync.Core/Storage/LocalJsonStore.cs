using System.Text.Json;
using System.Text.Json.Serialization;
using SchulnetzSync.Core.Comments;
using SchulnetzSync.Core.Grades;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Tasks;

namespace SchulnetzSync.Core.Storage;

/// <summary>
/// Keeps the user's data as one JSON file per data set in a profile's folder.
///
/// The file names and the JSON are what every earlier version wrote; changing either
/// would lose the data of everyone who updates.
/// </summary>
/// <param name="directory">The folder of the profile.</param>
public sealed class LocalJsonStore(string directory) : IUserDataStore
{
    public const string CachedEventsFile    = "cached-events.json";
    public const string ManualEventsFile    = "manual-events.json";
    public const string TasksFile           = "tasks.json";
    public const string TaskListsFile       = "task-lists.json";
    public const string SubjectCommentsFile = "subject-comments.json";
    public const string GradesFile          = "grades.json";

    /// <summary>Added to a file's name for the copy kept when it could not be read.</summary>
    public const string CorruptSuffix = ".corrupt";

    private static readonly JsonSerializerOptions s_plain = new();

    // Enums as strings, otherwise SchulnetzEventType does not survive the round trip
    private static readonly JsonSerializerOptions s_events = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters                  = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<SchulnetzEvent> LoadCachedEvents()
        => Load<List<SchulnetzEvent>>(CachedEventsFile, s_events) ?? [];

    public void SaveCachedEvents(IReadOnlyList<SchulnetzEvent> events)
        => Save(CachedEventsFile, events, s_events);

    public IReadOnlyList<ManualEventData> LoadManualEvents()
        => Load<List<ManualEventData>>(ManualEventsFile, s_plain) ?? [];

    public void SaveManualEvents(IReadOnlyList<ManualEventData> events)
        => Save(ManualEventsFile, events, s_plain);

    public IReadOnlyList<TaskItem> LoadTasks()
        => Load<List<TaskItem>>(TasksFile, s_plain) ?? [];

    public void SaveTasks(IReadOnlyList<TaskItem> tasks)
        => Save(TasksFile, tasks, s_plain);

    public IReadOnlyList<string> LoadTaskLists()
        => Load<List<string>>(TaskListsFile, s_plain) ?? [];

    public void SaveTaskLists(IReadOnlyList<string> names)
        => Save(TaskListsFile, names, s_plain);

    public IReadOnlyList<SubjectComment> LoadSubjectComments()
        => Load<List<SubjectComment>>(SubjectCommentsFile, s_plain) ?? [];

    public void SaveSubjectComments(IReadOnlyList<SubjectComment> comments)
        => Save(SubjectCommentsFile, comments, s_plain);

    public IReadOnlyList<GradeEntry> LoadGrades()
        => Load<List<GradeEntry>>(GradesFile, s_plain) ?? [];

    public void SaveGrades(IReadOnlyList<GradeEntry> grades)
        => Save(GradesFile, grades, s_plain);

    private T? Load<T>(string fileName, JsonSerializerOptions options) where T : class
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException ex)
        {
            // The caller may carry on with an empty list and save over this file, so keep
            // what is there: it can often be repaired by hand.
            File.Copy(path, path + CorruptSuffix, overwrite: true);
            throw new CorruptUserDataException(fileName, ex);
        }
    }

    // Written via a temporary file, so a crash never leaves half a data set behind.
    private void Save<T>(string fileName, T value, JsonSerializerOptions options)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var tmp  = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, options));
        File.Move(tmp, path, overwrite: true);
    }
}
