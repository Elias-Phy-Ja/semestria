namespace SchulnetzSync.Core.Grades;

/// <summary>
/// The arithmetic behind the grade page: subject averages, report rounding and what the
/// next exam needs to reach a target. Pure functions, no state.
/// </summary>
public static class Grading
{
    /// <summary>
    /// A report grade only moves in half steps. Half grades round up, the usual Swiss way:
    /// 4.25 becomes 4.5, 4.24 stays 4.
    /// </summary>
    public static double RoundToHalf(double average)
        => Math.Round(Math.Round(average * 2, 6, MidpointRounding.AwayFromZero),
                      MidpointRounding.AwayFromZero) / 2;

    /// <summary>One standing per subject that has at least one valid grade, sorted by code.</summary>
    public static IReadOnlyList<SubjectStanding> Summarize(IEnumerable<GradeEntry> grades)
        => grades
            .Where(g => g.IsValid)
            .GroupBy(g => g.Subject.Trim().ToUpperInvariant())
            .Select(g =>
            {
                double weight  = g.Sum(x => x.Weight);
                double average = g.Sum(x => x.Value * x.Weight) / weight;
                return new SubjectStanding(g.Key, average, RoundToHalf(average), g.Count(), weight);
            })
            .OrderBy(s => s.Subject, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

    /// <summary>Average of the rounded report grades, the way the report computes it.</summary>
    public static double? OverallAverage(IReadOnlyList<SubjectStanding> standings)
        => standings.Count == 0 ? null : standings.Average(s => s.Grade);

    /// <summary>Sum of the plus points over all subjects.</summary>
    public static double PlusPoints(IReadOnlyList<SubjectStanding> standings)
        => standings.Sum(s => s.PlusPoints);

    /// <summary>
    /// The grade the next exam needs so that the subject ends up at least at
    /// <paramref name="targetGrade"/> after rounding.
    ///
    /// The raw result is returned on purpose: above 6 means the target is out of reach
    /// with one exam, below 1 means it is safe whatever happens. The caller decides how
    /// to say that.
    /// </summary>
    /// <param name="standing">The subject as it is now; null when it has no grades yet.</param>
    /// <param name="nextWeight">Weight of the upcoming exam.</param>
    /// <param name="targetGrade">Report grade to reach, e.g. 4 or 5.</param>
    public static double NeededGrade(SubjectStanding? standing, double nextWeight, double targetGrade)
    {
        if (nextWeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(nextWeight), "Weight must be positive.");

        // Rounding to half grades means a quarter below the target is still enough.
        double threshold = targetGrade - 0.25;
        double weight    = standing?.TotalWeight ?? 0;
        double sum       = standing is null ? 0 : standing.Average * standing.TotalWeight;

        return (threshold * (weight + nextWeight) - sum) / nextWeight;
    }
}
