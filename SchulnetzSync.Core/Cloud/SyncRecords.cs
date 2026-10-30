using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SchulnetzSync.Core.Comments;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Tasks;

namespace SchulnetzSync.Core.Cloud;

/// <summary>Everything of a profile that takes part in the sync.</summary>
/// <param name="Tasks">Every task.</param>
/// <param name="TaskLists">Every list name.</param>
/// <param name="Comments">The notes on subjects.</param>
/// <param name="ManualEvents">Hand-made entries; only the exams among them are synced.</param>
/// <param name="FeedEvents">The parsed feed; only the exams among them are synced.</param>
/// <param name="Overlays">The user's changes to exams from the feed.</param>
/// <param name="Subjects">Subject codes, for the pickers in the web app.</param>
public sealed record SyncableData(
    IReadOnlyList<TaskItem>        Tasks,
    IReadOnlyList<string>          TaskLists,
    IReadOnlyList<SubjectComment>  Comments,
    IReadOnlyList<ManualEventData> ManualEvents,
    IReadOnlyList<SchulnetzEvent>  FeedEvents,
    IReadOnlyList<PruefungOverlay> Overlays,
    IReadOnlyList<string>          Subjects);

/// <summary>
/// An exam as it goes to the cloud: what the web app shows and nothing else. The raw UID
/// stays on the device.
/// </summary>
public sealed record PruefungContent(
    string Key,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string Summary,
    string? Location);

/// <summary>Turns the data of a profile into sync records and back. Pure functions.</summary>
public static class SyncRecords
{
    public const string SubjectListId = "subjects";

    private const string OverlayPrefix = "overlay_";
    private const string ManualPruefungType = "Pruefung";

    /// <summary>The record id of a list. It comes from the name, as lists have no id of their own.</summary>
    public static string TaskListId(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToUpperInvariant()));
        return "list_" + Convert.ToHexString(bytes, 0, 16);
    }

    public static string OverlayId(string key) => OverlayPrefix + key;

    public static IReadOnlyList<SyncRecord> From(SyncableData data)
    {
        var records = new List<SyncRecord>();

        void Add<T>(string id, SyncRecordKind kind, T value)
            => records.Add(new SyncRecord(id, kind, JsonSerializer.Serialize(value)));

        foreach (var task in data.Tasks)
            Add(task.Id.ToString("N"), SyncRecordKind.Task, task);

        foreach (var name in data.TaskLists.Distinct(StringComparer.OrdinalIgnoreCase))
            Add(TaskListId(name), SyncRecordKind.TaskList, name);

        foreach (var comment in data.Comments)
            Add(comment.Id.ToString("N"), SyncRecordKind.SubjectComment, comment);

        foreach (var ev in data.ManualEvents.Where(e => e.TypeKey == ManualPruefungType))
            Add(ev.Id.ToString("N"), SyncRecordKind.ManualPruefung, ev);

        foreach (var ev in data.FeedEvents.Where(e => e.Type == SchulnetzEventType.Pruefung))
            Add(ev.Key, SyncRecordKind.Pruefung,
                new PruefungContent(ev.Key, ev.Start, ev.End, ev.IsAllDay, ev.Summary, ev.Location));

        foreach (var overlay in data.Overlays)
            Add(OverlayId(overlay.Key), SyncRecordKind.PruefungOverlay, overlay);

        Add(SubjectListId, SyncRecordKind.SubjectList, data.Subjects);
        return records;
    }

    /// <summary>
    /// Takes in what came from the cloud. A record that cannot be read throws: the content
    /// was written by another program, and quietly skipping it would lose the change.
    /// </summary>
    public static SyncableData Apply(
        SyncableData data, IReadOnlyList<SyncRecord> changed, IReadOnlyList<SyncRef> removed)
    {
        var tasks    = data.Tasks.ToList();
        var lists    = data.TaskLists.ToList();
        var comments = data.Comments.ToList();
        var manual   = data.ManualEvents.ToList();
        var overlays = data.Overlays.ToList();

        foreach (var record in changed)
        {
            switch (record.Kind)
            {
                case SyncRecordKind.Task:
                    var task = Read<TaskItem>(record);
                    Check(record, task.Id.ToString("N"));
                    Upsert(tasks, task, t => t.Id == task.Id);
                    break;

                case SyncRecordKind.TaskList:
                    var name = Read<string>(record);
                    Check(record, TaskListId(name));
                    if (!lists.Contains(name, StringComparer.OrdinalIgnoreCase)) lists.Add(name);
                    break;

                case SyncRecordKind.SubjectComment:
                    var comment = Read<SubjectComment>(record);
                    Check(record, comment.Id.ToString("N"));
                    Upsert(comments, comment, c => c.Id == comment.Id);
                    break;

                case SyncRecordKind.ManualPruefung:
                    var ev = Read<ManualEventData>(record);
                    Check(record, ev.Id.ToString("N"));
                    Upsert(manual, ev, e => e.Id == ev.Id);
                    break;

                case SyncRecordKind.PruefungOverlay:
                    var overlay = Read<PruefungOverlay>(record);
                    Check(record, OverlayId(overlay.Key));
                    Upsert(overlays, overlay, o => o.Key == overlay.Key);
                    break;

                default:
                    throw new InvalidOperationException($"{record.Kind} kommt nur von der Store-App.");
            }
        }

        foreach (var gone in removed)
        {
            switch (gone.Kind)
            {
                case SyncRecordKind.Task:
                    tasks.RemoveAll(t => t.Id.ToString("N") == gone.Id);
                    break;

                case SyncRecordKind.TaskList:
                    // The tasks in it survive and move to "Ohne Liste", as in the app itself.
                    lists.RemoveAll(n => TaskListId(n) == gone.Id);
                    for (int i = 0; i < tasks.Count; i++)
                        if (tasks[i].ListName.Length > 0 && TaskListId(tasks[i].ListName) == gone.Id)
                            tasks[i] = tasks[i] with { ListName = "" };
                    break;

                case SyncRecordKind.SubjectComment:
                    comments.RemoveAll(c => c.Id.ToString("N") == gone.Id);
                    break;

                case SyncRecordKind.ManualPruefung:
                    manual.RemoveAll(e => e.Id.ToString("N") == gone.Id);
                    break;

                case SyncRecordKind.PruefungOverlay:
                    overlays.RemoveAll(o => OverlayId(o.Key) == gone.Id);
                    break;

                default:
                    throw new InvalidOperationException($"{gone.Kind} kommt nur von der Store-App.");
            }
        }

        return data with { Tasks = tasks, TaskLists = lists, Comments = comments, ManualEvents = manual, Overlays = overlays };
    }

    private static void Upsert<T>(List<T> list, T item, Predicate<T> same)
    {
        var i = list.FindIndex(same);
        if (i < 0) list.Add(item); else list[i] = item;
    }

    private static T Read<T>(SyncRecord record) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(record.Content)
                ?? throw new InvalidDataException($"Datensatz {record.Id} ist leer.");
        }
        catch (JsonException ex)
        {
            // The content stays out of the message, it is the user's data.
            throw new InvalidDataException($"Datensatz {record.Id} ({record.Kind}) ist ungültig.", ex);
        }
    }

    // A record whose content claims a different identity must not overwrite another one.
    private static void Check(SyncRecord record, string idFromContent)
    {
        if (record.Id != idFromContent)
            throw new InvalidDataException($"Datensatz {record.Id} ({record.Kind}) passt nicht zu seinem Inhalt.");
    }
}
