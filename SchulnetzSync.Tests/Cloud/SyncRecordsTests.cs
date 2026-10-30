using SchulnetzSync.Core.Cloud;
using SchulnetzSync.Core.Comments;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Tasks;
using Xunit;

namespace SchulnetzSync.Tests.Cloud;

public class SyncRecordsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(1));

    private static TaskItem Task(string list = "DEU")
        => new(Guid.NewGuid(), "Aufsatz", list, null, At, null, false, false, false, At, null);

    private static SchulnetzEvent Feed(string key, SchulnetzEventType type)
        => new(key, key + "_raw_uid", type, At, At.AddHours(1), false, "Mathe", "A1");

    private static ManualEventData Manual(string type)
        => new(Guid.NewGuid(), "Vortrag", At, At.AddHours(1), false, null, type);

    private static PruefungOverlay Overlay(string key)
        => new(key, "Neu", null, null, null, "Mathe", "A1", At, At.AddHours(1));

    private static readonly SyncableData Empty = new([], [], [], [], [], [], []);

    [Fact]
    public void Lektionen_NeverLeaveTheDevice()
    {
        var data = Empty with { FeedEvents = [Feed("L_1", SchulnetzEventType.Lektion)] };

        Assert.DoesNotContain(SyncRecords.From(data), r => r.Content.Contains("L_1"));
    }

    [Fact]
    public void Termine_NeverLeaveTheDevice()
    {
        var data = Empty with
        {
            FeedEvents   = [Feed("T_1", SchulnetzEventType.Termin)],
            ManualEvents = [Manual("Termin")]
        };

        Assert.Equal([SyncRecordKind.SubjectList], SyncRecords.From(data).Select(r => r.Kind));
    }

    [Fact]
    public void Pruefung_ComesWithoutTheRawUid()
    {
        var data = Empty with { FeedEvents = [Feed("P_65100", SchulnetzEventType.Pruefung)] };

        var record = Assert.Single(SyncRecords.From(data), r => r.Kind == SyncRecordKind.Pruefung);

        Assert.Equal("P_65100", record.Id);
        Assert.DoesNotContain("raw_uid", record.Content);
    }

    [Fact]
    public void Records_AreStableBetweenRuns()
    {
        var data = Empty with { Tasks = [Task()], TaskLists = ["DEU"], Subjects = ["DEU"] };

        Assert.Equal(
            SyncRecords.From(data).Select(r => r.Hash),
            SyncRecords.From(data).Select(r => r.Hash));
    }

    [Fact]
    public void ListId_IgnoresCaseAndSpaces()
    {
        Assert.Equal(SyncRecords.TaskListId("DEU"), SyncRecords.TaskListId(" deu "));
        Assert.NotEqual(SyncRecords.TaskListId("DEU"), SyncRecords.TaskListId("ENG"));
    }

    [Fact]
    public void RoundTrip_GivesBackTheSameData()
    {
        var comment = new SubjectComment(Guid.NewGuid(), "DEU", "Notiz", At, null);
        var data = Empty with
        {
            Tasks        = [Task()],
            TaskLists    = ["DEU"],
            Comments     = [comment],
            ManualEvents = [Manual("Pruefung")],
            Overlays     = [Overlay("P_1")]
        };
        var editable = SyncRecords.From(data).Where(r => !r.Kind.IsStoreOwned()).ToList();

        var result = SyncRecords.Apply(Empty, editable, []);

        Assert.Equal(data.Tasks, result.Tasks);
        Assert.Equal(data.TaskLists, result.TaskLists);
        Assert.Equal(data.Comments, result.Comments);
        Assert.Equal(data.ManualEvents, result.ManualEvents);
        Assert.Equal(data.Overlays, result.Overlays);
    }

    [Fact]
    public void Apply_ReplacesAnExistingTask()
    {
        var task    = Task();
        var edited  = task with { Title = "Im Web geändert" };
        var records = SyncRecords.From(Empty with { Tasks = [edited] }).Where(r => r.Kind == SyncRecordKind.Task).ToList();

        var result = SyncRecords.Apply(Empty with { Tasks = [task] }, records, []);

        Assert.Equal("Im Web geändert", Assert.Single(result.Tasks).Title);
    }

    [Fact]
    public void RemovingAList_KeepsItsTasks()
    {
        var data = Empty with { Tasks = [Task("DEU")], TaskLists = ["DEU"] };

        var result = SyncRecords.Apply(data, [], [new SyncRef(SyncRecords.TaskListId("DEU"), SyncRecordKind.TaskList)]);

        Assert.Empty(result.TaskLists);
        Assert.Equal("", Assert.Single(result.Tasks).ListName);
    }

    [Fact]
    public void Remove_TakesOutTheRecord()
    {
        var task = Task();

        var result = SyncRecords.Apply(
            Empty with { Tasks = [task] }, [], [new SyncRef(task.Id.ToString("N"), SyncRecordKind.Task)]);

        Assert.Empty(result.Tasks);
    }

    [Fact]
    public void BrokenContent_FailsVisiblyWithoutShowingIt()
    {
        var record = new SyncRecord("abc", SyncRecordKind.Task, "{geheimer text");

        var ex = Assert.Throws<InvalidDataException>(() => SyncRecords.Apply(Empty, [record], []));

        Assert.DoesNotContain("geheimer", ex.Message);
    }

    [Fact]
    public void ContentWithAnotherIdentity_IsRefused()
    {
        var content = SyncRecords.From(Empty with { Tasks = [Task()] }).First(r => r.Kind == SyncRecordKind.Task).Content;
        var record  = new SyncRecord(Guid.NewGuid().ToString("N"), SyncRecordKind.Task, content);

        Assert.Throws<InvalidDataException>(() => SyncRecords.Apply(Empty, [record], []));
    }

    [Theory]
    [InlineData(SyncRecordKind.Pruefung)]
    [InlineData(SyncRecordKind.SubjectList)]
    public void StoreOwnedKinds_CannotBeApplied(SyncRecordKind kind)
    {
        Assert.Throws<InvalidOperationException>(
            () => SyncRecords.Apply(Empty, [new SyncRecord("x", kind, "[]")], []));
    }

    [Fact]
    public void WebEdit_TravelsThroughMergeAndApply()
    {
        var data   = Empty with { Tasks = [Task()], Subjects = ["DEU"] };
        var first  = SyncMergerRun(data, [], []);
        var edited = data.Tasks[0] with { Title = "Vom Handy" };
        var remote = SyncRecords.From(Empty with { Tasks = [edited] })
            .Where(r => r.Kind == SyncRecordKind.Task)
            .Select(r => new RemoteRecord(r.Id, r.Kind, r.Content, false, At, At))
            .ToList();

        var second = SyncMergerRun(data, first.Ledger, remote);
        var result = SyncRecords.Apply(data, second.ApplyLocally, second.RemoveLocally);

        Assert.Equal("Vom Handy", Assert.Single(result.Tasks).Title);
        Assert.Empty(second.Uploads);
    }

    private static SyncMergePlan SyncMergerRun(
        SyncableData data, IReadOnlyList<SyncLedgerEntry> ledger, IReadOnlyList<RemoteRecord> remote)
        => SyncMerger.Merge(SyncRecords.From(data), ledger, remote, null, At);
}
