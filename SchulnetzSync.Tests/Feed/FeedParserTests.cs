using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Tests.Feed;

/// <summary>
/// <see cref="FeedParser"/> against the cleaned sample feed in the fixtures folder.
/// The two tests that matter most are 4 and 5: they pin down that the UID decides the
/// type and the SUMMARY has no say, which is exactly where the first version got it wrong.
/// </summary>
public class FeedParserTests
{
    // Parsed once for the whole class — the fixture never changes during a run.
    private static readonly IReadOnlyList<SchulnetzEvent> s_events = LoadSampleFeed();
    private static readonly string s_rawIcs = ReadFixture();

    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-feed.ics"));

    private static IReadOnlyList<SchulnetzEvent> LoadSampleFeed() =>
        FeedParser.Parse(ReadFixture());

    // ── 1. All three types come out right ──────────────────────────────

    [Fact]
    public void Parse_PruefungUid_ClassifiedAsPruefung()
    {
        // P_64554, title "TEU_I26A Prüfung 1.2_GEO_TEU"
        var ev = s_events.Single(e => e.Key == "P_64554");
        Assert.Equal(SchulnetzEventType.Pruefung, ev.Type);
    }

    [Fact]
    public void Parse_TerminUid_ClassifiedAsTermin()
    {
        // T_7409, "1. Mai Tag der Arbeit", an all-day entry
        var ev = s_events.Single(e => e.Key == "T_7409");
        Assert.Equal(SchulnetzEventType.Termin, ev.Type);
    }

    [Fact]
    public void Parse_LektionUid_ClassifiedAsLektion()
    {
        // Numeric UID, so a lesson: 48167 at 13:05 on 2026-08-10.
        var ev = s_events.First(e => e.Type == SchulnetzEventType.Lektion);
        Assert.Equal(SchulnetzEventType.Lektion, ev.Type);
        // The key is the whole string before the @ — the bare number repeats every week.
        Assert.Contains("et", ev.Key);
    }

    // ── 2. All-day entry: flag set, start and end where they belong ────

    [Fact]
    public void Parse_AllDayEvent_IsAllDayTrue()
    {
        var ev = s_events.Single(e => e.Key == "T_7409");
        Assert.True(ev.IsAllDay);
    }

    [Fact]
    public void Parse_AllDayEvent_StartIsMidnightOnCorrectDate()
    {
        // DTSTART;VALUE=DATE:20260501 → 2026-05-01 00:00
        var ev = s_events.Single(e => e.Key == "T_7409");
        Assert.Equal(new DateOnly(2026, 5, 1), DateOnly.FromDateTime(ev.Start.DateTime));
        Assert.Equal(TimeOnly.MinValue, TimeOnly.FromDateTime(ev.Start.DateTime));
    }

    [Fact]
    public void Parse_AllDayEvent_DurationP1D_EndIsNextDay()
    {
        // DURATION:P1D → End = Start + 1 day = 2026-05-02
        var ev = s_events.Single(e => e.Key == "T_7409");
        Assert.Equal(new DateOnly(2026, 5, 2), DateOnly.FromDateTime(ev.End.DateTime));
    }

    // ── 3. Multi-day entry spans exactly its DURATION ──────────────────

    [Fact]
    public void Parse_MultiDayEvent_DurationP4D_EndIsFourDaysLater()
    {
        // T_7554: DTSTART;VALUE=DATE:20260407, DURATION:P4D → End=2026-04-11
        var ev = s_events.Single(e => e.Key == "T_7554");
        Assert.True(ev.IsAllDay);
        Assert.Equal(new DateOnly(2026, 4, 7), DateOnly.FromDateTime(ev.Start.DateTime));
        Assert.Equal(new DateOnly(2026, 4, 11), DateOnly.FromDateTime(ev.End.DateTime));
        Assert.Equal(TimeSpan.FromDays(4), ev.End - ev.Start);
    }

    // ── 4. SUMMARY says "Prüfung", UID says T_ — the UID wins ──────────

    [Fact]
    public void Parse_TerminUidWithPruefungInSummary_ClassifiedAsTermin()
    {
        // T_7554: "Prüfungsvorbereitungskurse Gymnasium/WMS/IMS" — T_ UID wins.
        var ev = s_events.Single(e => e.Key == "T_7554");
        Assert.Contains("Prüfung", ev.Summary, StringComparison.Ordinal);
        Assert.Equal(SchulnetzEventType.Termin, ev.Type);
    }

    // ── 5. No "Prüfung" in the title, P_ UID — still an exam ───────────

    [Fact]
    public void Parse_PruefungUidWithoutPruefungInSummary_ClassifiedAsPruefung()
    {
        // P_65081: "TEU_I26A Bio" — no "Prüfung" in title, P_ UID wins.
        var ev = s_events.Single(e => e.Key == "P_65081");
        Assert.DoesNotContain("Prüfung", ev.Summary, StringComparison.Ordinal);
        Assert.Equal(SchulnetzEventType.Pruefung, ev.Type);
    }

    // ── 6. A cut-off feed has to be noticed ────────────────────────────

    [Fact]
    public void CheckPlausibility_TruncatedFeed_ReportsProblem()
    {
        // A download that stopped early and never reached END:VCALENDAR.
        string truncated = s_rawIcs[..^50]; // lop off the tail
        var health = FeedParser.CheckPlausibility(truncated);
        Assert.False(health.IsHealthy);
        Assert.NotEmpty(health.Problems);
    }

    [Fact]
    public void CheckPlausibility_ValidFeed_IsHealthy()
    {
        var health = FeedParser.CheckPlausibility(s_rawIcs);
        Assert.True(health.IsHealthy);
        Assert.Empty(health.Problems);
    }

    [Fact]
    public void CheckPlausibility_EmptyContent_ReportsBothProblems()
    {
        var health = FeedParser.CheckPlausibility("BEGIN:VCALENDAR\r\nEND:VCALENDAR");
        // Well-formed, but empty — so exactly one complaint, not two.
        Assert.False(health.IsHealthy);
        Assert.Single(health.Problems);
    }

    // ── 7. Empty LOCATION becomes null, a filled one survives ──────────

    [Fact]
    public void Parse_EmptyLocation_IsNull()
    {
        // Exams come out of the feed without a room.
        var ev = s_events.Single(e => e.Key == "P_64554");
        Assert.Null(ev.Location);
    }

    [Fact]
    public void Parse_FilledLocation_IsPreserved()
    {
        // A lesson on 2026-09-07 at 14:55, room 039.
        var ev = s_events.Single(e => e.Key == "20260907et48175et14:55et15:40et039");
        Assert.Equal("039", ev.Location);
    }

    // ── 8. Timed entries carry the Zurich offset ───────────────────────

    [Fact]
    public void Parse_TimedEvent_OffsetIsZurichSummerTime()
    {
        // DTSTART;TZID=Europe/Zurich:20260907T145500 — September is CEST, so +02:00.
        var ev = s_events.Single(e => e.Key == "P_65100");
        Assert.Equal(14, ev.Start.Hour);
        Assert.Equal(55, ev.Start.Minute);
        Assert.Equal(TimeSpan.FromHours(2), ev.Start.Offset);
    }

    // ── 9. The exam key is exactly the second UID segment ──────────────

    [Fact]
    public void Parse_PruefungKey_IsSecondUidSegment()
    {
        var ev = s_events.Single(e => e.Key == "P_65100");
        Assert.Equal("P_65100", ev.Key);
        // The key has to appear in the raw UID, otherwise it was invented somewhere.
        Assert.Contains("P_65100", ev.RawUid, StringComparison.Ordinal);
    }
}
