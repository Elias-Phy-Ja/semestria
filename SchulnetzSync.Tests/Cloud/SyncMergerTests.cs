using SchulnetzSync.Core.Cloud;
using Xunit;

namespace SchulnetzSync.Tests.Cloud;

/// <summary>The rule above all others: the Store app has the highest authority.</summary>
public class SyncMergerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static SyncRecord Local(string id, string content, SyncRecordKind kind = SyncRecordKind.Task)
        => new(id, kind, content);

    private static SyncLedgerEntry Agreed(string id, string content, SyncRecordKind kind = SyncRecordKind.Task)
        => new(id, kind, SyncRecord.HashOf(content), Now.AddDays(-1), null);

    private static SyncLedgerEntry Tombstone(string id, DateTimeOffset at)
        => new(id, SyncRecordKind.Task, string.Empty, at, at);

    private static RemoteRecord Remote(string id, string content, SyncRecordKind kind = SyncRecordKind.Task)
        => new(id, kind, content, false, Now.AddHours(-1), Now.AddHours(-1));

    private static RemoteRecord RemoteDeleted(string id)
        => new(id, SyncRecordKind.Task, null, true, Now.AddHours(-1), Now.AddHours(-1));

    private static SyncMergePlan Merge(
        IReadOnlyList<SyncRecord>? local = null,
        IReadOnlyList<SyncLedgerEntry>? ledger = null,
        IReadOnlyList<RemoteRecord>? remote = null,
        DateTimeOffset? cursor = null)
        => SyncMerger.Merge(local ?? [], ledger ?? [], remote ?? [], cursor, Now);

    private static void AssertNothingToDo(SyncMergePlan plan)
    {
        Assert.Empty(plan.Uploads);
        Assert.Empty(plan.Tombstones);
        Assert.Empty(plan.ApplyLocally);
        Assert.Empty(plan.RemoveLocally);
        Assert.Empty(plan.PurgeRemote);
        Assert.Equal(0, plan.Conflicts);
    }

    [Fact]
    public void FirstSync_UploadsEverything()
    {
        var plan = Merge(local: [Local("a", "1"), Local("b", "2")]);

        Assert.Equal(["a", "b"], plan.Uploads.Select(r => r.Id));
        Assert.Equal(2, plan.Ledger.Count);
    }

    [Fact]
    public void NothingChanged_DoesNothing()
    {
        var plan = Merge(local: [Local("a", "1")], ledger: [Agreed("a", "1")]);

        AssertNothingToDo(plan);
        Assert.Single(plan.Ledger);
    }

    [Fact]
    public void LocalEdit_IsUploaded()
    {
        var plan = Merge(local: [Local("a", "neu")], ledger: [Agreed("a", "alt")]);

        Assert.Equal("neu", Assert.Single(plan.Uploads).Content);
        Assert.Equal(SyncRecord.HashOf("neu"), Assert.Single(plan.Ledger).Hash);
    }

    [Fact]
    public void LocalDelete_BecomesATombstone()
    {
        var plan = Merge(ledger: [Agreed("a", "1")]);

        Assert.Equal("a", Assert.Single(plan.Tombstones).Id);
        Assert.Equal(Now, Assert.Single(plan.Ledger).DeletedAt);
    }

    [Fact]
    public void WebEdit_IsTakenOver()
    {
        var plan = Merge(local: [Local("a", "alt")], ledger: [Agreed("a", "alt")], remote: [Remote("a", "web")]);

        Assert.Equal("web", Assert.Single(plan.ApplyLocally).Content);
        Assert.Empty(plan.Uploads);
        Assert.Equal(SyncRecord.HashOf("web"), Assert.Single(plan.Ledger).Hash);
    }

    [Fact]
    public void WebCreate_IsTakenOver()
    {
        var plan = Merge(remote: [Remote("a", "web")]);

        Assert.Equal("a", Assert.Single(plan.ApplyLocally).Id);
    }

    [Fact]
    public void WebDelete_RemovesLocally()
    {
        var plan = Merge(local: [Local("a", "1")], ledger: [Agreed("a", "1")], remote: [RemoteDeleted("a")]);

        Assert.Equal("a", Assert.Single(plan.RemoveLocally).Id);
        Assert.NotNull(Assert.Single(plan.Ledger).DeletedAt);
    }

    [Fact]
    public void BothEdited_TheStoreAppWins()
    {
        var plan = Merge(local: [Local("a", "app")], ledger: [Agreed("a", "alt")], remote: [Remote("a", "web")]);

        Assert.Equal("app", Assert.Single(plan.Uploads).Content);
        Assert.Empty(plan.ApplyLocally);
        Assert.Equal(1, plan.Conflicts);
    }

    [Fact]
    public void DeletedInAppEditedOnWeb_TheStoreAppWins()
    {
        var plan = Merge(ledger: [Agreed("a", "alt")], remote: [Remote("a", "web")]);

        Assert.Equal("a", Assert.Single(plan.Tombstones).Id);
        Assert.Empty(plan.ApplyLocally);
        Assert.Equal(1, plan.Conflicts);
    }

    [Fact]
    public void EditedInAppDeletedOnWeb_TheStoreAppWins()
    {
        var plan = Merge(local: [Local("a", "app")], ledger: [Agreed("a", "alt")], remote: [RemoteDeleted("a")]);

        Assert.Equal("app", Assert.Single(plan.Uploads).Content);
        Assert.Empty(plan.RemoveLocally);
    }

    [Fact]
    public void SameEditOnBothSides_IsNoConflict()
    {
        var plan = Merge(local: [Local("a", "gleich")], ledger: [Agreed("a", "alt")], remote: [Remote("a", "gleich")]);

        AssertNothingToDo(plan);
        Assert.Equal(SyncRecord.HashOf("gleich"), Assert.Single(plan.Ledger).Hash);
    }

    [Fact]
    public void EchoOfOwnUpload_DoesNothing()
    {
        var plan = Merge(local: [Local("a", "1")], ledger: [Agreed("a", "1")], remote: [Remote("a", "1")]);

        AssertNothingToDo(plan);
    }

    [Fact]
    public void EchoOfOwnTombstone_DoesNothing()
    {
        var plan = Merge(ledger: [Tombstone("a", Now.AddDays(-1))], remote: [RemoteDeleted("a")]);

        AssertNothingToDo(plan);
        Assert.Equal(Now.AddDays(-1), Assert.Single(plan.Ledger).DeletedAt);
    }

    [Fact]
    public void RecordCreatedOnWebAfterOurDelete_IsTakenOver()
    {
        var plan = Merge(ledger: [Tombstone("a", Now.AddDays(-1))], remote: [Remote("a", "wieder da")]);

        Assert.Equal("wieder da", Assert.Single(plan.ApplyLocally).Content);
        Assert.Null(Assert.Single(plan.Ledger).DeletedAt);
    }

    [Theory]
    [InlineData(SyncRecordKind.Pruefung)]
    [InlineData(SyncRecordKind.SubjectList)]
    public void StoreOwnedRecord_ChangedOnWeb_IsOverwritten(SyncRecordKind kind)
    {
        var plan = Merge(
            local:  [Local("P_1", "feed", kind)],
            ledger: [Agreed("P_1", "feed", kind)],
            remote: [Remote("P_1", "web", kind)]);

        Assert.Equal("feed", Assert.Single(plan.Uploads).Content);
        Assert.Empty(plan.ApplyLocally);
    }

    [Fact]
    public void StoreOwnedRecord_UnknownHere_IsDeletedInTheCloud()
    {
        var plan = Merge(remote: [Remote("P_1", "alt", SyncRecordKind.Pruefung)]);

        Assert.Equal("P_1", Assert.Single(plan.Tombstones).Id);
        Assert.Empty(plan.ApplyLocally);
    }

    [Fact]
    public void Tombstone_StaysForItsLifetime()
    {
        var plan = Merge(ledger: [Tombstone("a", Now - SyncMerger.TombstoneLifetime + TimeSpan.FromDays(1))]);

        Assert.Empty(plan.PurgeRemote);
        Assert.Single(plan.Ledger);
    }

    [Fact]
    public void Tombstone_IsPurgedAfterItsLifetime()
    {
        var plan = Merge(ledger: [Tombstone("a", Now - SyncMerger.TombstoneLifetime)]);

        Assert.Equal("a", Assert.Single(plan.PurgeRemote).Id);
        Assert.Empty(plan.Ledger);
    }

    [Fact]
    public void ReconnectOnNewPc_TakesOverTheCloud()
    {
        var plan = Merge(remote: [Remote("a", "1"), Remote("b", "2")]);

        Assert.Equal(2, plan.ApplyLocally.Count);
        Assert.Equal(0, plan.Conflicts);
    }

    [Fact]
    public void ReconnectWithSameData_ChangesNothing()
    {
        var plan = Merge(local: [Local("a", "1")], remote: [Remote("a", "1")]);

        AssertNothingToDo(plan);
        Assert.Single(plan.Ledger);
    }

    [Fact]
    public void Cursor_MovesToTheNewestServerTime()
    {
        var older = Remote("a", "1") with { ServerTime = Now.AddHours(-3) };
        var newer = Remote("b", "2") with { ServerTime = Now.AddHours(-2) };

        var plan = Merge(remote: [older, newer], cursor: Now.AddDays(-1));

        Assert.Equal(Now.AddHours(-2), plan.Cursor);
    }

    [Fact]
    public void Cursor_StaysWithoutChanges()
    {
        Assert.Equal(Now.AddDays(-1), Merge(cursor: Now.AddDays(-1)).Cursor);
        Assert.Null(Merge().Cursor);
    }

    [Fact]
    public void WrongDeviceClock_CannotDecideAConflict()
    {
        var fromTheFuture = Remote("a", "web") with { UpdatedAt = Now.AddYears(5) };

        var plan = Merge(local: [Local("a", "app")], ledger: [Agreed("a", "alt")], remote: [fromTheFuture]);

        Assert.Equal("app", Assert.Single(plan.Uploads).Content);
    }

    [Fact]
    public void RemoteRecordWithoutContent_FailsVisibly()
    {
        var broken = new RemoteRecord("a", SyncRecordKind.Task, null, false, Now, Now);

        Assert.Throws<InvalidDataException>(() => Merge(remote: [broken]));
    }

    [Fact]
    public void SyncingAgainWithTheNewLedger_DoesNothing()
    {
        var first = Merge(local: [Local("a", "1"), Local("b", "2")], ledger: [Agreed("c", "3")]);

        var second = Merge(local: [Local("a", "1"), Local("b", "2")], ledger: first.Ledger);

        AssertNothingToDo(second);
    }
}
