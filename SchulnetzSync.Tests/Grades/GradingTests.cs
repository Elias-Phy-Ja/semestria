using System.Text.Json;
using SchulnetzSync.Core.Grades;

namespace SchulnetzSync.Tests.Grades;

/// <summary>
/// Averages, report rounding, plus points, the target calculation and both promotion
/// systems, on made-up grades.
/// </summary>
public class GradingTests
{
    private static GradeEntry G(string subject, double value, double weight = 1)
        => new(Guid.NewGuid(), subject, value, weight, "", new DateOnly(2026, 10, 1));

    private static IReadOnlyList<SubjectStanding> Standings(params GradeEntry[] grades)
        => Grading.Summarize(grades);

    // ── Rounding and averages ───────────────────────────────────────────

    [Theory]
    [InlineData(4.24, 4.0)]
    [InlineData(4.25, 4.5)]
    [InlineData(4.74, 4.5)]
    [InlineData(4.75, 5.0)]
    [InlineData(3.75, 4.0)]
    [InlineData(6.0,  6.0)]
    public void RoundToHalf_RoundsHalfGradesUp(double avg, double expected)
        => Assert.Equal(expected, Grading.RoundToHalf(avg));

    [Fact]
    public void Summarize_WeighsGrades()
    {
        var s = Standings(G("MAT", 6, 1), G("MAT", 3, 2)).Single();

        Assert.Equal(4.0, s.Average, 6);
        Assert.Equal(4.0, s.Grade);
        Assert.Equal(3.0, s.TotalWeight);
    }

    [Fact]
    public void Summarize_MergesSubjectCodesCaseInsensitive_AndSkipsInvalid()
    {
        var s = Standings(G("inf", 5), G("INF", 6), G("INF", 7), G("INF", 4, weight: 0));

        Assert.Single(s);
        Assert.Equal(2, s[0].Count);
    }

    [Theory]
    [InlineData(6.0,  2.0)]
    [InlineData(5.0,  1.0)]
    [InlineData(4.5,  0.5)]
    [InlineData(4.0,  0.0)]
    [InlineData(3.5, -1.0)]
    [InlineData(3.0, -2.0)]
    [InlineData(2.0, -4.0)]
    public void PlusPoints_HalfGradeAboveIsHalfPoint_HalfGradeBelowIsMinusOne(double grade, double expected)
        => Assert.Equal(expected, Standings(G("DEU", grade)).Single().PlusPoints);

    // ── Target calculation ──────────────────────────────────────────────

    [Fact]
    public void NeededGrade_UsesTheRoundingMargin()
    {
        // One 3.5 so far; a second exam of the same weight needs 4.0 to land on 3.75 → 4
        var s = Standings(G("INF", 3.5)).Single();

        Assert.Equal(4.0, Grading.NeededGrade(s, 1, 4), 6);
    }

    [Fact]
    public void NeededGrade_RespectsTheNextExamsWeight()
    {
        // 4 with weight 2; a half-weight exam to reach a 5 needs 4.75 * 2.5 - 8 = 3.875 / 0.5
        var s = Standings(G("MAT", 4, 2)).Single();

        Assert.Equal(7.75, Grading.NeededGrade(s, 0.5, 5), 6);
    }

    [Fact]
    public void NeededGrade_WithoutGrades_IsTheThresholdItself()
        => Assert.Equal(3.75, Grading.NeededGrade(null, 1, 4), 6);

    [Fact]
    public void NeededGrade_RejectsZeroWeight()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Grading.NeededGrade(null, 0, 4));

    // ── System 1: plus points ───────────────────────────────────────────

    [Fact]
    public void PlusPointsSystem_PassesWithBalancedGrades()
    {
        // +1 +0.5 −1 = +0.5, one subject below 4
        var result = PromotionRule.PlusPointsSystem.Evaluate(
            Standings(G("DEU", 5), G("ENG", 4.5), G("MAT", 3.5)));

        Assert.Equal(ConditionStatus.Met, result.Status);
    }

    [Fact]
    public void PlusPointsSystem_FailsOnNegativePoints()
    {
        // +0.5 −2 = −1.5
        var result = PromotionRule.PlusPointsSystem.Evaluate(
            Standings(G("DEU", 4.5), G("MAT", 3)));

        Assert.Equal(ConditionStatus.Failed, result.Status);
        Assert.Equal(ConditionStatus.Failed, result.Conditions[0].Status);
    }

    [Fact]
    public void PlusPointsSystem_FailsOnThreeInsufficientSubjects_EvenWithEnoughPoints()
    {
        var result = PromotionRule.PlusPointsSystem.Evaluate(Standings(
            G("DEU", 6), G("ENG", 6), G("FRA", 6),
            G("MAT", 3.5), G("PHY", 3.5), G("CHE", 3.5)));

        Assert.Equal(ConditionStatus.Met,    result.Conditions[0].Status);
        Assert.Equal(ConditionStatus.Failed, result.Conditions[1].Status);
        Assert.Equal(ConditionStatus.Failed, result.Status);
    }

    // ── System 2: overall average plus key subject ──────────────────────

    private static PromotionRule ImsRule => PromotionRule.AverageSystem with
    {
        Conditions = [.. PromotionRule.AverageSystem.Conditions, new MinSubjectGrade("INF", 4)]
    };

    [Fact]
    public void AverageSystem_PassesWithAverageFourAndInformatikFour()
    {
        var result = ImsRule.Evaluate(Standings(G("INF", 4), G("DEU", 4.5), G("MAT", 3.5)));

        Assert.Equal(ConditionStatus.Met, result.Status);
    }

    [Fact]
    public void AverageSystem_FailsWhenInformatikIsBelowFour_EvenWithAGoodAverage()
    {
        var result = ImsRule.Evaluate(Standings(G("INF", 3.5), G("DEU", 6), G("ENG", 6)));

        Assert.Equal(ConditionStatus.Failed, result.Status);
        Assert.Equal(ConditionStatus.Failed, result.Conditions[2].Status);
    }

    [Fact]
    public void AverageSystem_FailsBelowAverageFour()
    {
        var result = ImsRule.Evaluate(Standings(G("INF", 4), G("DEU", 3.5)));

        Assert.Equal(ConditionStatus.Failed, result.Conditions[0].Status);
    }

    [Fact]
    public void KeySubjectWithoutGrades_IsOpen_NotFailed()
    {
        var result = ImsRule.Evaluate(Standings(G("DEU", 5)));

        Assert.Equal(ConditionStatus.Open, result.Conditions[2].Status);
        Assert.Equal(ConditionStatus.Open, result.Status);
    }

    // ── Custom rules survive being saved ────────────────────────────────

    [Fact]
    public void CustomRule_RoundTripsThroughJson()
    {
        var rule = new PromotionRule("Eigene Regel",
        [
            new MinOverallAverage(4.5),
            new MaxInsufficientSubjects(1),
            new MinPlusPoints(1),
            new MinSubjectGrade("MAT", 4)
        ]);

        var back = JsonSerializer.Deserialize<PromotionRule>(JsonSerializer.Serialize(rule))!;

        Assert.Equal(rule.Name, back.Name);
        Assert.Equal(rule.Conditions, back.Conditions);
    }
}
