using SchulnetzSync.Core.Model;
using Xunit;

namespace SchulnetzSync.Tests.Model;

/// <summary>The user's change holds until the school changes the same thing.</summary>
public class PruefungOverlayTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 8, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset End   = Start.AddHours(1);

    private static SchulnetzEvent Exam(
        string title = "Mathe", string? room = "A1", DateTimeOffset? start = null, DateTimeOffset? end = null)
        => new("P_1", "P_1_raw", SchulnetzEventType.Pruefung, start ?? Start, end ?? End, false, title, room);

    private static PruefungOverlay Overlay(
        string? title = null, string? room = null, DateTimeOffset? start = null, DateTimeOffset? end = null)
        => new("P_1", title, room, start, end, "Mathe", "A1", Start, End);

    [Fact]
    public void Change_IsLaidOverTheFeed()
    {
        var result = PruefungOverlays.Apply([Exam()], [Overlay(title: "Mathe Kapitel 3", room: "B2")]);

        var ev = Assert.Single(result.Events);
        Assert.Equal("Mathe Kapitel 3", ev.Summary);
        Assert.Equal("B2", ev.Location);
        Assert.Single(result.Overlays);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void Key_StaysTheSame()
    {
        var moved = Start.AddDays(1);

        var result = PruefungOverlays.Apply([Exam()], [Overlay(start: moved, end: moved.AddHours(1))]);

        Assert.Equal("P_1", Assert.Single(result.Events).Key);
        Assert.Equal(moved, result.Events[0].Start);
    }

    [Fact]
    public void EmptyRoom_RemovesTheRoom()
    {
        var result = PruefungOverlays.Apply([Exam()], [Overlay(room: "")]);

        Assert.Null(Assert.Single(result.Events).Location);
        Assert.Single(result.Overlays);
    }

    [Fact]
    public void SchoolChangesTheSameField_SchoolWinsWithANotice()
    {
        var result = PruefungOverlays.Apply([Exam(room: "C3")], [Overlay(room: "B2")]);

        Assert.Equal("C3", Assert.Single(result.Events).Location);
        Assert.Empty(result.Overlays);
        Assert.Equal(new OverlayNotice("P_1", OverlayField.Location), Assert.Single(result.Notices));
    }

    [Fact]
    public void SchoolMovesTheExam_SchoolWinsWithANotice()
    {
        var moved  = Start.AddDays(2);
        var mine   = Start.AddDays(1);

        var result = PruefungOverlays.Apply(
            [Exam(start: moved, end: moved.AddHours(1))],
            [Overlay(start: mine, end: mine.AddHours(1))]);

        Assert.Equal(moved, Assert.Single(result.Events).Start);
        Assert.Equal(OverlayField.Time, Assert.Single(result.Notices).Field);
    }

    [Fact]
    public void SchoolChangesAnotherField_ChangeStays()
    {
        var result = PruefungOverlays.Apply([Exam(room: "C3")], [Overlay(title: "Mein Titel")]);

        var ev = Assert.Single(result.Events);
        Assert.Equal("Mein Titel", ev.Summary);
        Assert.Equal("C3", ev.Location);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void SchoolArrivesAtTheSameValue_NoNotice()
    {
        var result = PruefungOverlays.Apply([Exam(room: "B2")], [Overlay(room: "B2")]);

        Assert.Equal("B2", Assert.Single(result.Events).Location);
        Assert.Empty(result.Overlays);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void OnlyTheChangedFieldGivesWay()
    {
        var result = PruefungOverlays.Apply([Exam(room: "C3")], [Overlay(title: "Mein Titel", room: "B2")]);

        var left = Assert.Single(result.Overlays);
        Assert.Equal("Mein Titel", left.Title);
        Assert.Null(left.Location);
    }

    [Fact]
    public void ExamMissingFromTheFeed_KeepsItsOverlay()
    {
        var result = PruefungOverlays.Apply([], [Overlay(title: "Mein Titel")]);

        Assert.Single(result.Overlays);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void OnlyExamsTakeAnOverlay()
    {
        var termin = Exam() with { Type = SchulnetzEventType.Termin };

        var result = PruefungOverlays.Apply([termin], [Overlay(title: "Mein Titel")]);

        Assert.Equal("Mathe", Assert.Single(result.Events).Summary);
    }

    [Fact]
    public void OtherEvents_PassThroughUntouched()
    {
        var other = Exam() with { Key = "P_2" };

        Assert.Equal(other, Assert.Single(PruefungOverlays.Apply([other], []).Events));
    }
}
