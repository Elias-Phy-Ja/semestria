using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Tests.Feed;

/// <summary>
/// <see cref="LessonTracker.Update"/> on made-up timetables: the feed drops a cancelled
/// lesson without a trace, so every rule here is about telling a cancellation apart from
/// a lesson that simply moved, passed, or belongs to an old timetable.
/// </summary>
public class LessonTrackerTests
{
    // Monday morning; the lessons below all lie later that week
    private static readonly DateTimeOffset Now =
        new(2026, 10, 19, 7, 0, 0, TimeSpan.FromHours(2));

    private static SchulnetzEvent Lektion(
        int day, int hour, string room = "014", string subject = "DEU")
    {
        var start = Now.Date.AddDays(day).AddHours(hour);
        var s     = new DateTimeOffset(start, Now.Offset);
        var key   = $"{s:yyyyMMdd}et48167et{s:HH:mm}et{s.AddMinutes(45):HH:mm}et{room}";
        return new SchulnetzEvent(key, key + "@centerboard.ch", SchulnetzEventType.Lektion,
            s, s.AddMinutes(45), false, $"{subject}_I26A_SmiJa", room);
    }

    private static IReadOnlyList<KnownLesson> Known(params SchulnetzEvent[] lessons)
        => LessonTracker.Seed(lessons);

    [Fact]
    public void VanishedUpcomingLesson_IsMarkedCancelled()
    {
        var mon = Lektion(0, 8);
        var tue = Lektion(1, 8);

        var result = LessonTracker.Update(Known(mon, tue), [mon], Now);

        Assert.Equal(2, result.Count);
        Assert.False(result.Single(k => k.Lesson.Key == mon.Key).IsCancelled);
        Assert.True(result.Single(k => k.Lesson.Key == tue.Key).IsCancelled);
    }

    [Fact]
    public void FirstRun_WithoutSnapshot_MarksNothing()
    {
        var result = LessonTracker.Update([], [Lektion(0, 8)], Now);

        Assert.All(result, k => Assert.False(k.IsCancelled));
    }

    [Fact]
    public void VanishedPastLesson_IsDroppedNotCancelled()
    {
        // The feed trims old lessons; that is no cancellation.
        var past = new SchulnetzEvent("old", "old", SchulnetzEventType.Lektion,
            Now.AddDays(-3), Now.AddDays(-3).AddMinutes(45), false, "DEU_I26A", "014");
        var next = Lektion(0, 8);

        var result = LessonTracker.Update(Known(past, next), [next], Now);

        Assert.DoesNotContain(result, k => k.Lesson.Key == "old");
    }

    [Fact]
    public void RoomChange_IsNotACancellation()
    {
        var before = Lektion(1, 8, room: "014");
        var after  = Lektion(1, 8, room: "220");

        var result = LessonTracker.Update(Known(before), [after], Now);

        Assert.Single(result);
        Assert.Equal(after.Key, result[0].Lesson.Key);
        Assert.False(result[0].IsCancelled);
    }

    [Fact]
    public void CancelledLesson_StaysCancelledOnLaterRefreshes()
    {
        var mon = Lektion(0, 8);
        var tue = Lektion(1, 8);

        var first  = LessonTracker.Update(Known(mon, tue), [mon], Now);
        // Next refresh happens after the lesson would have taken place
        var second = LessonTracker.Update(first, [mon], Now.AddDays(3));

        Assert.True(second.Single(k => k.Lesson.Key == tue.Key).IsCancelled);
    }

    [Fact]
    public void CancelledLesson_ThatComesBack_IsNoLongerCancelled()
    {
        var mon = Lektion(0, 8);
        var tue = Lektion(1, 8);

        var first  = LessonTracker.Update(Known(mon, tue), [mon], Now);
        var second = LessonTracker.Update(first, [mon, tue], Now);

        Assert.All(second, k => Assert.False(k.IsCancelled));
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void CancelledLesson_IsForgottenAfterRetention()
    {
        var mon = Lektion(0, 8);
        var tue = Lektion(1, 8);

        var first = LessonTracker.Update(Known(mon, tue), [mon], Now);
        var later = LessonTracker.Update(first, [mon],
            Now.AddDays(2) + LessonTracker.CancelledRetention);

        Assert.DoesNotContain(later, k => k.Lesson.Key == tue.Key);
    }

    [Fact]
    public void MostOfTheTimetableVanishing_IsANewTimetable_NotCancellations()
    {
        var old = Enumerable.Range(0, 20).Select(i => Lektion(i / 5, 8 + i % 5)).ToArray();
        var replacement = Lektion(0, 8, subject: "MAT", room: "300");
        // Same slot as old[0], so give the replacement a different one
        replacement = replacement with { Start = Now.AddDays(10), End = Now.AddDays(10).AddMinutes(45) };

        var result = LessonTracker.Update(Known(old), [replacement], Now);

        Assert.DoesNotContain(result, k => k.IsCancelled);
        Assert.Single(result);
    }

    [Fact]
    public void EmptyFeed_MarksNothing()
    {
        var result = LessonTracker.Update(Known(Lektion(0, 8), Lektion(1, 8)), [], Now);

        Assert.Empty(result);
    }

    [Fact]
    public void AFewCancellations_InABigTimetable_AreAllMarked()
    {
        var all  = Enumerable.Range(0, 30).Select(i => Lektion(i / 6, 8 + i % 6)).ToArray();
        var kept = all.Skip(12).ToArray();   // 12 of 30 vanish: below half

        var result = LessonTracker.Update(Known(all), kept, Now);

        Assert.Equal(12, result.Count(k => k.IsCancelled));
    }

    [Fact]
    public void NonLessons_AreIgnored()
    {
        var exam = new SchulnetzEvent("P_1", "P_1", SchulnetzEventType.Pruefung,
            Now.AddDays(1), Now.AddDays(1).AddHours(1), false, "DEU Prüfung", null);

        var result = LessonTracker.Update([], [exam, Lektion(0, 8)], Now);

        Assert.Single(result);
        Assert.Equal(SchulnetzEventType.Lektion, result[0].Lesson.Type);
    }
}
