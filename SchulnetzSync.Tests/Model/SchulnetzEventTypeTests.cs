using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Tests.Model;

/// <summary>
/// Basic sanity tests for the model types introduced in Phase 1.
/// Parser tests live in FeedParserTests (Phase 2).
/// </summary>
public class SchulnetzEventTypeTests
{
    [Fact]
    public void Enum_HasExactlyThreeMembers()
    {
        var values = Enum.GetValues<SchulnetzEventType>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void SchulnetzEvent_RecordEquality_BasedOnAllProperties()
    {
        var a = new SchulnetzEvent(
            Key: "P_65100",
            RawUid: "20260907etP_65100et14:55et15:40etPruefung@centerboard.ch",
            Type: SchulnetzEventType.Pruefung,
            Start: new DateTimeOffset(2026, 9, 7, 14, 55, 0, TimeSpan.FromHours(2)),
            End: new DateTimeOffset(2026, 9, 7, 15, 40, 0, TimeSpan.FromHours(2)),
            IsAllDay: false,
            Summary: "Mathematik",
            Location: null);

        var b = a with { };   // structural copy

        Assert.Equal(a, b);
        Assert.NotSame(a, b);
    }

    [Fact]
    public void SyncOptions_Defaults_ExcludeLektion()
    {
        var opts = new SyncOptions();

        Assert.Contains(SchulnetzEventType.Pruefung, opts.EnabledTypes);
        Assert.Contains(SchulnetzEventType.Termin, opts.EnabledTypes);
        Assert.DoesNotContain(SchulnetzEventType.Lektion, opts.EnabledTypes);
    }

    [Fact]
    public void SyncOptions_Defaults_PrimaryCalendar()
    {
        var opts = new SyncOptions();
        Assert.Null(opts.CalendarId);
    }

    [Fact]
    public void SyncOptions_Defaults_CancelInsteadOfDelete_IsTrue()
    {
        var opts = new SyncOptions();
        Assert.True(opts.CancelInsteadOfDelete);
    }

    [Fact]
    public void SyncOptions_Defaults_EnrichExamLocation_IsTrue()
    {
        var opts = new SyncOptions();
        Assert.True(opts.EnrichExamLocationFromLesson);
    }
}
