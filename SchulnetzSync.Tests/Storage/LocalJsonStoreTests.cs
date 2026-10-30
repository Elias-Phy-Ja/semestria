using SchulnetzSync.Core.Comments;
using SchulnetzSync.Core.Grades;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Storage;
using SchulnetzSync.Core.Tasks;
using Xunit;

namespace SchulnetzSync.Tests.Storage;

/// <summary>
/// The store sits on top of the files of everyone who updates. The JSON constants below are
/// what version 3.1 wrote: they must load, and saving must give back exactly the same bytes.
/// </summary>
public sealed class LocalJsonStoreTests : IDisposable
{
    private const string TasksV31 =
        """[{"Id":"11111111-1111-1111-1111-111111111111","Title":"M\u00E4rchen lesen","ListName":"DEU","Notes":null,"DueAt":"2026-11-02T08:30:00+01:00","RemindAt":null,"ReminderShown":false,"IsImportant":true,"IsDone":false,"CreatedAt":"2026-10-30T18:00:00+01:00","CompletedAt":null,"ListLabel":"DEU"}]""";

    private const string TaskListsV31 = """["DEU","WIR"]""";

    private const string CommentsV31 =
        """[{"Id":"22222222-2222-2222-2222-222222222222","Subject":"TEU","Text":"Kapitel 3","CreatedAt":"2026-10-30T18:00:00+01:00","EditedAt":null}]""";

    private const string ManualV31 =
        """[{"Id":"33333333-3333-3333-3333-333333333333","Title":"Vortrag","Start":"2026-11-03T10:00:00+01:00","End":"2026-11-03T11:00:00+01:00","IsAllDay":false,"Location":null,"TypeKey":"Pruefung"}]""";

    private const string GradesV31 =
        """[{"Id":"44444444-4444-4444-4444-444444444444","Subject":"INF","Value":5.5,"Weight":1,"Title":"Pr\u00FCfung 1","Date":"2026-10-30","PeriodId":"55555555-5555-5555-5555-555555555555","IsValid":true}]""";

    private const string EventsV31 =
        """[{"Key":"P_65100","RawUid":"P_65100_20261103","Type":"Pruefung","Start":"2026-11-03T08:00:00+01:00","End":"2026-11-03T09:00:00+01:00","IsAllDay":false,"Summary":"Pr\u00FCfung Mathe","Location":"A1"}]""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "semestria-store-" + Guid.NewGuid().ToString("N"));
    private readonly LocalJsonStore _store;

    public LocalJsonStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new LocalJsonStore(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Put(string name, string content) => File.WriteAllText(Path.Combine(_dir, name), content);
    private string Get(string name) => File.ReadAllText(Path.Combine(_dir, name));

    [Fact]
    public void Tasks_FromV31_LoadAndSaveUnchanged()
    {
        Put(LocalJsonStore.TasksFile, TasksV31);

        var tasks = _store.LoadTasks();
        _store.SaveTasks(tasks);

        var task = Assert.Single(tasks);
        Assert.Equal("Märchen lesen", task.Title);
        Assert.True(task.IsImportant);
        Assert.Equal(TasksV31, Get(LocalJsonStore.TasksFile));
    }

    [Fact]
    public void TaskLists_FromV31_LoadAndSaveUnchanged()
    {
        Put(LocalJsonStore.TaskListsFile, TaskListsV31);

        var names = _store.LoadTaskLists();
        _store.SaveTaskLists(names);

        Assert.Equal(["DEU", "WIR"], names);
        Assert.Equal(TaskListsV31, Get(LocalJsonStore.TaskListsFile));
    }

    [Fact]
    public void Comments_FromV31_LoadAndSaveUnchanged()
    {
        Put(LocalJsonStore.SubjectCommentsFile, CommentsV31);

        var comments = _store.LoadSubjectComments();
        _store.SaveSubjectComments(comments);

        Assert.Equal("TEU", Assert.Single(comments).Subject);
        Assert.Equal(CommentsV31, Get(LocalJsonStore.SubjectCommentsFile));
    }

    [Fact]
    public void ManualEvents_FromV31_LoadAndSaveUnchanged()
    {
        Put(LocalJsonStore.ManualEventsFile, ManualV31);

        var events = _store.LoadManualEvents();
        _store.SaveManualEvents(events);

        Assert.Equal("Pruefung", Assert.Single(events).TypeKey);
        Assert.Equal(ManualV31, Get(LocalJsonStore.ManualEventsFile));
    }

    [Fact]
    public void Grades_FromV31_LoadAndSaveUnchanged()
    {
        Put(LocalJsonStore.GradesFile, GradesV31);

        var grades = _store.LoadGrades();
        _store.SaveGrades(grades);

        var grade = Assert.Single(grades);
        Assert.Equal(5.5, grade.Value);
        Assert.Equal(new DateOnly(2026, 10, 30), grade.Date);
        Assert.Equal(GradesV31, Get(LocalJsonStore.GradesFile));
    }

    [Fact]
    public void GradesWithoutPeriod_FromBeforePeriods_StillLoad()
    {
        Put(LocalJsonStore.GradesFile,
            """[{"Id":"44444444-4444-4444-4444-444444444444","Subject":"INF","Value":4,"Weight":0.5,"Title":"","Date":"2026-01-10"}]""");

        Assert.Null(Assert.Single(_store.LoadGrades()).PeriodId);
    }

    [Fact]
    public void CachedEvents_KeepTheirTypeAsText()
    {
        Put(LocalJsonStore.CachedEventsFile, EventsV31);

        var events = _store.LoadCachedEvents();
        _store.SaveCachedEvents(events);

        Assert.Equal(SchulnetzEventType.Pruefung, Assert.Single(events).Type);
        Assert.Equal(EventsV31, Get(LocalJsonStore.CachedEventsFile));
    }

    [Fact]
    public void NothingStored_LoadsEmpty()
    {
        Assert.Empty(_store.LoadTasks());
        Assert.Empty(_store.LoadTaskLists());
        Assert.Empty(_store.LoadSubjectComments());
        Assert.Empty(_store.LoadManualEvents());
        Assert.Empty(_store.LoadGrades());
        Assert.Empty(_store.LoadCachedEvents());
    }

    [Fact]
    public void JsonNull_LoadsEmpty()
    {
        Put(LocalJsonStore.TasksFile, "null");

        Assert.Empty(_store.LoadTasks());
    }

    [Fact]
    public void DamagedFile_ThrowsAndKeepsACopy()
    {
        Put(LocalJsonStore.TasksFile, "[{\"Id\":");

        var ex = Assert.Throws<CorruptUserDataException>(() => _store.LoadTasks());

        Assert.Equal(LocalJsonStore.TasksFile, ex.FileName);
        Assert.Equal("[{\"Id\":", Get(LocalJsonStore.TasksFile + LocalJsonStore.CorruptSuffix));
    }

    [Fact]
    public void DamagedFile_CopySurvivesTheNextSave()
    {
        Put(LocalJsonStore.TasksFile, "kaputt");
        Assert.Throws<CorruptUserDataException>(() => _store.LoadTasks());

        _store.SaveTasks([]);

        Assert.Equal("[]", Get(LocalJsonStore.TasksFile));
        Assert.Equal("kaputt", Get(LocalJsonStore.TasksFile + LocalJsonStore.CorruptSuffix));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        _store.SaveTaskLists(["DEU"]);
        _store.SaveTaskLists(["DEU", "ENG"]);

        Assert.Equal([LocalJsonStore.TaskListsFile], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_CreatesTheFolder()
    {
        var store = new LocalJsonStore(Path.Combine(_dir, "neu", "profil"));

        store.SaveTaskLists(["DEU"]);

        Assert.Equal(["DEU"], store.LoadTaskLists());
    }

    [Fact]
    public void Profiles_DoNotSeeEachOther()
    {
        var other = new LocalJsonStore(Path.Combine(_dir, "anderes-profil"));

        _store.SaveTaskLists(["DEU"]);

        Assert.Empty(other.LoadTaskLists());
    }

    [Fact]
    public void RoundTrip_KeepsEveryField()
    {
        var at   = new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.FromHours(1));
        var task = new TaskItem(Guid.NewGuid(), "Aufsatz", "DEU", "Notiz", at, at, true, true, true, at, at);
        var note = new SubjectComment(Guid.NewGuid(), "DEU", "Text", at, at);

        _store.SaveTasks([task]);
        _store.SaveSubjectComments([note]);

        Assert.Equal(task, Assert.Single(_store.LoadTasks()));
        Assert.Equal(note, Assert.Single(_store.LoadSubjectComments()));
    }
}
