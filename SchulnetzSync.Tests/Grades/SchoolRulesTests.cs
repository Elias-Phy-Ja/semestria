using System.Text.Json;
using SchulnetzSync.Core.Grades;

namespace SchulnetzSync.Tests.Grades;

/// <summary>
/// The two school presets, checked against the worked examples the schools publish:
/// the table in «Allgemeine Informationen zur Matur 2026» (Alte Kantonsschule Aarau) and
/// the «Notenrechner BM IMS Aarau 2026» sheet. Plus the semester / school-year periods.
/// </summary>
public class SchoolRulesTests
{
    private static GradeEntry G(string subject, double value)
        => new(Guid.NewGuid(), subject, value, 1, "", new DateOnly(2026, 6, 1));

    // ── Kanti Aarau, Matur ──────────────────────────────────────────────

    /// <summary>The «Beispiel» table of the Matur info: 2 below 4, plus 6.5, minus ×2 = 3, saldo 3.5.</summary>
    private static IReadOnlyList<SubjectStanding> KantiExample() => Grading.Summarize(
    [
        G("DEU", 5), G("FRA", 4), G("ENG", 5), G("MAT", 4.5), G("BIO", 3.5), G("CHE", 4.5), G("PHY", 3),
        G("GSC", 4), G("GEO", 4), G("BIG", 5), G("SF", 5), G("EF", 4.5), G("MA", 5)
    ]);

    [Fact]
    public void KantiExample_MatchesTheSchoolsTable()
    {
        var s = KantiExample();

        Assert.Equal(3.5, Grading.PlusPoints(s));                 // «Saldo 3.5»
        Assert.Equal(2, s.Count(x => x.IsInsufficient));          // «Anzahl Noten unter 4: 2»
        Assert.Equal(ConditionStatus.Met, PromotionRule.KantiMatur.Evaluate(s).Status);
    }

    [Fact]
    public void Kanti_AllowsFourGradesBelowFour_ButNotFive()
    {
        var four = Grading.Summarize([G("A", 6), G("B", 6), G("C", 6), G("D", 3.5), G("E", 3.5), G("F", 3.5), G("G", 3.5)]);
        var five = Grading.Summarize([G("A", 6), G("B", 6), G("C", 6), G("D", 3.5), G("E", 3.5), G("F", 3.5), G("G", 3.5), G("H", 3.5)]);

        Assert.Equal(ConditionStatus.Met,    PromotionRule.KantiMatur.Evaluate(four).Status);
        Assert.Equal(ConditionStatus.Failed, PromotionRule.KantiMatur.Evaluate(five).Status);
    }

    [Fact]
    public void Kanti_MinusPointsCountDouble()
    {
        // +1 against 3.5 (−0.5, doubled −1): saldo exactly 0 passes; one more half below fails
        Assert.Equal(ConditionStatus.Met,    PromotionRule.KantiMatur.Evaluate(Grading.Summarize([G("A", 5), G("B", 3.5)])).Status);
        Assert.Equal(ConditionStatus.Failed, PromotionRule.KantiMatur.Evaluate(Grading.Summarize([G("A", 5), G("B", 3)])).Status);
    }

    [Theory]
    [InlineData(4.75, 5.0)]    // Deutsch in the table
    [InlineData(4.125, 4.0)]   // Französisch
    [InlineData(4.625, 4.5)]   // Mathematik
    [InlineData(4.25, 4.5)]    // Ergänzungsfach: an exact quarter goes up
    public void Kanti_RoundingOfTheMaturGrade(double mean, double expected)
        => Assert.Equal(expected, Grading.RoundToHalf(mean));

    // ── IMS Aarau, Berufsmatur ──────────────────────────────────────────

    /// <summary>The «Fachnoten» column of the IMS sheet: Gesamtnote 4.2, two below 4, 1 Minuspunkt.</summary>
    private static List<GradeEntry> ImsExample() =>
    [
        G("DEU", 4), G("FRA", 3.5), G("ENG", 5), G("MAT", 4), G("FRW", 3.5),
        G("WR", 4), G("GP", 4), G("TU", 4.5), G("IDPA", 5)
    ];

    [Fact]
    public void ImsExample_MatchesTheSchoolsSheet()
    {
        var result = PromotionRule.ImsBm.Evaluate(Grading.Summarize([.. ImsExample(), G("INF", 4.5)]));

        Assert.Equal("4.2", result.Conditions[0].Detail);              // «Gesamtnote 4.2»
        Assert.Equal("1 Minuspunkte", result.Conditions[2].Detail);     // «Summe 1»
        Assert.Equal(ConditionStatus.Met, result.Status);
    }

    [Fact]
    public void Ims_FailsOnMoreThanThreeMinusPoints()
    {
        // Three subjects at 3 cost exactly 3 Minuspunkte, still allowed; 2.5, 3 and 3 cost 3.5
        var ok   = Grading.Summarize([G("A", 6), G("B", 6), G("C", 6), G("D", 3), G("E", 3), G("F", 3)]);
        var fail = Grading.Summarize([G("A", 6), G("B", 6), G("C", 6), G("D", 2.5), G("E", 3), G("F", 3)]);

        Assert.Equal(ConditionStatus.Met,    PromotionRule.ImsBm.Evaluate(ok).Conditions[2].Status);
        Assert.Equal(ConditionStatus.Failed, PromotionRule.ImsBm.Evaluate(fail).Conditions[2].Status);
    }

    [Fact]
    public void Ims_TemplateHasNoSubjectMinimum()
    {
        var result = PromotionRule.ImsBm.Evaluate(Grading.Summarize([G("DEU", 5), G("ENG", 5), G("INF", 3.5)]));

        Assert.DoesNotContain(PromotionRule.ImsBm.Conditions, c => c is MinSubjectGrade);
        Assert.Equal(ConditionStatus.Met, result.Status);
    }

    [Fact]
    public void Ims_OldUnchangedTemplate_IsUpgraded()
    {
        var old = new PromotionRule("IMS Aarau · BM",
            [new MinOverallAverage(4), new MaxInsufficientSubjects(2), new MaxDeficitPoints(2), new MinSubjectGrade("INF", 4)]);

        Assert.Same(PromotionRule.ImsBm, PromotionRule.UpgradeOutdatedTemplate(old));
    }

    [Fact]
    public void Ims_EditedRule_IsKept()
    {
        var edited = new PromotionRule("IMS Aarau · BM",
            [new MinOverallAverage(4), new MaxInsufficientSubjects(2), new MaxDeficitPoints(2), new MinSubjectGrade("INF", 4.5)]);

        Assert.Same(edited, PromotionRule.UpgradeOutdatedTemplate(edited));
    }

    [Fact]
    public void OverallAverage_CountsRoundedToATenth()
    {
        // 47.5 / 12 = 3.958…, which the report shows as 4.0
        var grades = Enumerable.Range(0, 11).Select(i => G($"S{i}", 4)).Append(G("X", 3.5)).ToList();

        var result = new MinOverallAverage(4).Evaluate(Grading.Summarize(grades));

        Assert.Equal(ConditionStatus.Met, result.Status);
        Assert.Equal("4.0", result.Detail);
    }

    [Fact]
    public void SchoolPresets_SurviveBeingSaved()
    {
        foreach (var rule in new[] { PromotionRule.KantiMatur, PromotionRule.ImsBm })
        {
            var back = JsonSerializer.Deserialize<PromotionRule>(JsonSerializer.Serialize(rule))!;
            Assert.Equal(rule.Conditions, back.Conditions);
        }
    }

    // ── Periods ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2026, 8, 20, PeriodKind.Semester, "1. Semester 2026/27")]
    [InlineData(2027, 1, 15, PeriodKind.Semester, "1. Semester 2026/27")]
    [InlineData(2027, 2, 10, PeriodKind.Semester, "2. Semester 2026/27")]
    [InlineData(2026, 7, 1,  PeriodKind.Semester, "2. Semester 2025/26")]
    [InlineData(2026, 10, 24, PeriodKind.Year,    "Schuljahr 2026/27")]
    [InlineData(2026, 3, 1,  PeriodKind.Year,     "Schuljahr 2025/26")]
    [InlineData(2099, 9, 1,  PeriodKind.Year,     "Schuljahr 2099/00")]
    public void SuggestedNames_FollowTheSwissSchoolYear(int y, int m, int d, PeriodKind kind, string expected)
        => Assert.Equal(expected, GradePeriods.SuggestName(kind, new DateTimeOffset(y, m, d, 12, 0, 0, TimeSpan.FromHours(2))));

    [Fact]
    public void SuggestNext_SkipsToTheNextFreeSemesterOrYear()
    {
        var oct = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));
        var state = GradePeriods.Start(null, PeriodKind.Semester, "1. Semester 2026/27", oct);

        Assert.Equal("2. Semester 2026/27", GradePeriods.SuggestNext(state, PeriodKind.Semester, oct));

        state = GradePeriods.Start(state, PeriodKind.Semester, "2. Semester 2026/27", oct);
        Assert.Equal("1. Semester 2027/28", GradePeriods.SuggestNext(state, PeriodKind.Semester, oct));

        state = GradePeriods.Start(state, PeriodKind.Year, "Schuljahr 2026/27", oct);
        Assert.Equal("Schuljahr 2027/28", GradePeriods.SuggestNext(state, PeriodKind.Year, oct));
    }

    [Fact]
    public void SuggestNext_WithNothingTaken_IsTheCurrentPeriod()
        => Assert.Equal("1. Semester 2026/27",
            GradePeriods.SuggestNext(null, PeriodKind.Semester, new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2))));

    [Fact]
    public void Migrate_PutsOldGradesIntoAFirstPeriod()
    {
        var old = new[] { G("DEU", 5), G("MAT", 4) };
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));

        var (state, grades) = GradePeriods.Migrate(null, old, now);

        Assert.NotNull(state);
        Assert.Single(state!.Periods);
        Assert.Equal("1. Semester 2026/27", state.Current.Name);
        Assert.All(grades, g => Assert.Equal(state.CurrentId, g.PeriodId));
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));
        var (state, grades) = GradePeriods.Migrate(null, [G("DEU", 5)], now);

        var (again, gradesAgain) = GradePeriods.Migrate(state!, grades, now.AddDays(30));

        Assert.Equal(state, again);
        Assert.Equal(grades, gradesAgain);
    }

    [Fact]
    public void StartingANewPeriod_KeepsTheOldGrades_ButStartsEmpty()
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));
        var (migrated, grades) = GradePeriods.Migrate(null, [G("DEU", 5), G("MAT", 4)], now);
        var state = migrated!;
        var first = state.CurrentId;

        state = GradePeriods.Start(state, PeriodKind.Semester, "  2. Semester 2026/27 ", now.AddMonths(4));

        Assert.Equal(2, state.Periods.Count);
        Assert.NotEqual(first, state.CurrentId);
        Assert.Equal("2. Semester 2026/27", state.Current.Name);
        Assert.Empty(grades.In(state.CurrentId));
        Assert.Equal(2, grades.In(first).Count());
    }

    [Fact]
    public void RemovingTheRunningPeriod_HandsOverToTheLatestRemaining()
    {
        var t = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));
        var state = GradePeriods.Start(null, PeriodKind.Semester, "1. Semester 2026/27", t);
        var first = state.CurrentId;
        state = GradePeriods.Start(state, PeriodKind.Semester, "2. Semester 2026/27", t.AddMonths(4));

        state = GradePeriods.Remove(state, state.CurrentId)!;

        Assert.Single(state.Periods);
        Assert.Equal(first, state.CurrentId);
    }

    [Fact]
    public void RemovingAnOlderPeriod_KeepsTheRunningOne()
    {
        var t = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));
        var state = GradePeriods.Start(null, PeriodKind.Semester, "1. Semester 2026/27", t);
        var first = state.CurrentId;
        state = GradePeriods.Start(state, PeriodKind.Semester, "2. Semester 2026/27", t.AddMonths(4));
        var second = state.CurrentId;

        state = GradePeriods.Remove(state, first)!;

        Assert.Equal(second, state.CurrentId);
        Assert.Single(state.Periods);
    }

    [Fact]
    public void RemovingTheOnlyPeriod_LeavesNone()
    {
        var t = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));
        var state = GradePeriods.Start(null, PeriodKind.Year, "Schuljahr 2026/27", t);

        Assert.Null(GradePeriods.Remove(state, state.CurrentId));
    }

    [Fact]
    public void NewUser_StartsWithoutAnyPeriod()
    {
        var (state, grades) = GradePeriods.Migrate(null, [], DateTimeOffset.Now);

        Assert.Null(state);
        Assert.Empty(grades);
    }

    [Fact]
    public void EmptyStoredState_CountsAsNoPeriod()
    {
        var (state, _) = GradePeriods.Migrate(new GradePeriodState([], Guid.NewGuid()), [], DateTimeOffset.Now);

        Assert.Null(state);
    }

    [Fact]
    public void StartingAPeriodWithoutName_UsesTheSuggestion()
    {
        var now = new DateTimeOffset(2027, 2, 10, 12, 0, 0, TimeSpan.FromHours(1));

        var state = GradePeriods.Start(null, PeriodKind.Year, "", now);

        Assert.Equal("Schuljahr 2026/27", state.Current.Name);
    }

    [Fact]
    public void PeriodState_RoundTripsThroughJson()
    {
        var state = GradePeriods.Start(null, PeriodKind.Year, "Schuljahr 2026/27", DateTimeOffset.Now);

        var json = JsonSerializer.Serialize(state);
        var back = JsonSerializer.Deserialize<GradePeriodState>(json)!;

        Assert.Equal(state.CurrentId, back.CurrentId);
        Assert.Equal(state.Periods, back.Periods);
        Assert.Contains("\"Year\"", json);
        Assert.DoesNotContain("\"Current\"", json);
    }

    [Fact]
    public void OldGradeFiles_WithoutPeriod_StillLoad()
    {
        const string json = """[{"Id":"0b6a7a51-4d0f-4d55-9a64-2f7c33c6e0a1","Subject":"DEU","Value":5,"Weight":1,"Title":"","Date":"2026-10-01"}]""";

        var grade = JsonSerializer.Deserialize<List<GradeEntry>>(json)!.Single();

        Assert.Null(grade.PeriodId);
    }
}
