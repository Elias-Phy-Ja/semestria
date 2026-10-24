using System.Text.Json.Serialization;

namespace SchulnetzSync.Core.Grades;

/// <summary>How the final grades of a school are put together.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FinalMethod
{
    /// <summary>
    /// Alte Kantonsschule Aarau, Matur: Vorschlagsnote = report grade of the last year
    /// (Geografie: mean of the last two years), Prüfungsnote = unrounded mean of written and
    /// oral, Maturnote = their mean rounded to half grades.
    /// </summary>
    KantiMatur,

    /// <summary>
    /// IMS Aarau, Berufsmatur: Erfahrungsnote = mean of all semester report grades rounded
    /// to a tenth, Prüfungsnote = mean of written and oral rounded to a tenth, Fachnote =
    /// their mean rounded to half grades.
    /// </summary>
    ImsBm
}

/// <summary>The final exam of one subject, as typed in. Everything is optional.</summary>
/// <param name="Subject">Subject code, e.g. "DEU".</param>
/// <param name="Written">Written exam, whole or half grade.</param>
/// <param name="Oral">Oral exam, whole or half grade.</param>
/// <param name="BaseOverride">
/// A Vorschlags- or Erfahrungsnote of one's own instead of the computed one: for subjects
/// whose earlier years are not in the app, or special cases like IDPA/IDAF at the IMS.
/// </param>
public sealed record ExamEntry(string Subject, double? Written, double? Oral, double? BaseOverride);

/// <summary>Everything about the final exams of one profile.</summary>
public sealed record FinalExamState(FinalMethod Method, IReadOnlyList<ExamEntry> Exams)
{
    public static FinalExamState Empty(FinalMethod method) => new(method, []);
}

/// <summary>One period's report: its name and the subject grades in it.</summary>
public sealed record ReportCard(string PeriodName, IReadOnlyList<SubjectStanding> Standings);

/// <summary>A subject on the final certificate, with how its grade came about.</summary>
/// <param name="Subject">Subject code.</param>
/// <param name="Base">Vorschlagsnote (Kanti) or Erfahrungsnote (IMS); null when nothing is known.</param>
/// <param name="BaseSource">Where <paramref name="Base"/> came from, in words.</param>
/// <param name="Exam">The Prüfungsnote; null when there is no exam (yet).</param>
/// <param name="Final">The final grade; null while <paramref name="Base"/> is missing.</param>
/// <param name="Unrounded">The mean before rounding, for display.</param>
public sealed record FinalSubject(
    string  Subject,
    double? Base,
    string  BaseSource,
    double? Exam,
    double? Final,
    double? Unrounded)
{
    public bool IsComplete => Final is not null;
}

/// <summary>The arithmetic of Matur and Berufsmatur. Pure, everything comes in as parameters.</summary>
public static class FinalGrades
{
    /// <summary>The subject code whose Kanti Vorschlagsnote is the mean of the last two years.</summary>
    public const string KantiGeografie = "GEO";

    /// <summary>Excel's ROUND(x, 1): tenths, halves away from zero.</summary>
    public static double RoundToTenth(double x)
        => Math.Round(Math.Round(x, 6), 1, MidpointRounding.AwayFromZero);

    /// <summary>The rule a final certificate is judged by.</summary>
    public static PromotionRule RuleFor(FinalMethod method)
        => method == FinalMethod.KantiMatur ? PromotionRule.KantiMatur : PromotionRule.ImsBm;

    /// <summary>
    /// Every subject that has report grades or an exam entry, with its final grade.
    /// </summary>
    /// <param name="reports">All periods, oldest first.</param>
    public static IReadOnlyList<FinalSubject> Compute(
        FinalMethod method, IReadOnlyList<ReportCard> reports, IReadOnlyList<ExamEntry> exams)
    {
        var subjects = reports.SelectMany(r => r.Standings.Select(s => s.Subject))
            .Concat(exams.Select(e => e.Subject.Trim().ToUpperInvariant()))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal);

        var result = new List<FinalSubject>();
        foreach (var subject in subjects)
        {
            var exam = exams.FirstOrDefault(e => string.Equals(e.Subject.Trim(), subject, StringComparison.OrdinalIgnoreCase));

            var (baseGrade, source) = exam?.BaseOverride is double own
                ? (own, "eigener Wert")
                : BaseFromReports(method, subject, reports);

            double? examGrade = ExamGrade(method, exam);

            double? unrounded = baseGrade is double b
                ? examGrade is double p ? (b + p) / 2 : b
                : null;
            double? final = unrounded is double u ? Grading.RoundToHalf(u) : null;

            result.Add(new FinalSubject(subject, baseGrade, source, examGrade, final, unrounded));
        }
        return result.AsReadOnly();
    }

    /// <summary>The complete subjects as standings, ready for <see cref="PromotionRule.Evaluate"/>.</summary>
    public static IReadOnlyList<SubjectStanding> AsStandings(IEnumerable<FinalSubject> subjects)
        => subjects
            .Where(s => s.IsComplete)
            .Select(s => new SubjectStanding(s.Subject, s.Unrounded!.Value, s.Final!.Value, 1, 1))
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// The exam average needed for a final grade of at least <paramref name="target"/>.
    /// Above 6 is out of reach; at or below 1 the target is safe whatever happens.
    /// </summary>
    public static double NeededExam(FinalMethod method, double baseGrade, double target)
    {
        // Rounding to half grades: a quarter below the target is still enough
        double needed = 2 * (target - 0.25) - baseGrade;
        // At the IMS the exam grade itself is rounded to a tenth, so a value that rounds up suffices
        return method == FinalMethod.ImsBm ? needed - 0.05 + 1e-9 : needed;
    }

    private static (double?, string) BaseFromReports(FinalMethod method, string subject, IReadOnlyList<ReportCard> reports)
    {
        var grades = reports
            .Select(r => (r.PeriodName, Grade: r.Standings.FirstOrDefault(s => string.Equals(s.Subject, subject, StringComparison.OrdinalIgnoreCase))?.Grade))
            .Where(x => x.Grade is not null)
            .Select(x => (x.PeriodName, Grade: x.Grade!.Value))
            .ToList();

        if (grades.Count == 0) return (null, "keine Zeugnisnote");

        if (method == FinalMethod.ImsBm)
        {
            var mean = RoundToTenth(grades.Average(g => g.Grade));
            return (mean, grades.Count == 1 ? $"aus {grades[0].PeriodName}" : $"Mittel aus {grades.Count} Zeugnissen");
        }

        if (string.Equals(subject, KantiGeografie, StringComparison.OrdinalIgnoreCase) && grades.Count >= 2)
        {
            var lastTwo = grades.TakeLast(2).ToList();
            return (Grading.RoundToHalf(lastTwo.Average(g => g.Grade)), "Mittel der letzten zwei Jahre");
        }

        var last = grades[^1];
        return (last.Grade, $"Zeugnis {last.PeriodName}");
    }

    private static double? ExamGrade(FinalMethod method, ExamEntry? exam)
    {
        var parts = new[] { exam?.Written, exam?.Oral }.Where(x => x is not null).Select(x => x!.Value).ToList();
        if (parts.Count == 0) return null;

        double mean = parts.Average();
        // Kanti: «ungerundetes Mittel»; IMS: rounded to a tenth like every intermediate grade there
        return method == FinalMethod.ImsBm ? RoundToTenth(mean) : mean;
    }

    /// <summary>Exam grades are given in whole or half grades only.</summary>
    public static bool IsValidExamGrade(double value)
        => value is >= GradeEntry.Min and <= GradeEntry.Max && Math.Abs(value * 2 - Math.Round(value * 2)) < 1e-9;
}
