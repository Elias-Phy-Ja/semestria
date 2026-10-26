using System.Text.Json;
using SchulnetzSync.Core.Grades;

namespace SchulnetzSync.Tests.Grades;

/// <summary>
/// Matur and Berufsmatur grades, row by row against the schools' own worked examples:
/// the table in «Allgemeine Informationen zur Matur 2026» (Alte Kantonsschule Aarau) and
/// the «Notenrechner BM IMS Aarau 2026» sheet.
/// </summary>
public class FinalGradesTests
{
    private static SubjectStanding S(string subject, double grade) => new(subject, grade, grade, 1, 1);

    private static ReportCard Report(string name, params (string Subject, double Grade)[] grades)
        => new(name, grades.Select(g => S(g.Subject, g.Grade)).ToList());

    private static ExamEntry Exam(string subject, double? written, double? oral = null, double? own = null)
        => new(subject, written, oral, own);

    private static FinalSubject One(IReadOnlyList<FinalSubject> all, string subject)
        => all.Single(s => s.Subject == subject);

    // ── Kanti Aarau ─────────────────────────────────────────────────────

    /// <summary>
    /// The «Beispiel» table: Vorschlagsnote of the 4th class, the exam as the mean of written
    /// and oral (both half grades), and the Maturnote rounded to half grades.
    /// </summary>
    [Theory]
    [InlineData("DEU", 5.0, 4.5, 4.5, 5.0)]    // V 5,   P 4.5   → 4.75  → 5
    [InlineData("FRA", 4.0, 4.0, 4.5, 4.0)]    // V 4,   P 4.25  → 4.125 → 4
    [InlineData("ENG", 4.5, 5.0, 5.0, 5.0)]    // V 4.5, P 5     → 4.75  → 5
    [InlineData("MAT", 4.5, 4.5, 5.0, 4.5)]    // V 4.5, P 4.75  → 4.625 → 4.5
    [InlineData("SF",  5.0, 5.0, 5.5, 5.0)]    // V 5,   P 5.25  → 5.125 → 5
    [InlineData("EF",  4.0, 4.5, null, 4.5)]   // V 4,   P 4.5   → 4.25  → 4.5 (quarter goes up)
    public void Kanti_TableOfTheMaturInfo(string subject, double vorschlag, double written, double? oral, double expected)
    {
        var result = FinalGrades.Compute(FinalMethod.KantiMatur,
            [Report("Schuljahr 2025/26", (subject, vorschlag))],
            [Exam(subject, written, oral)]);

        Assert.Equal(expected, One(result, subject).Final);
    }

    [Fact]
    public void Kanti_ExamIsTheUnroundedMean()
    {
        var result = FinalGrades.Compute(FinalMethod.KantiMatur,
            [Report("4. Klasse", ("FRA", 4))], [Exam("FRA", 4, 4.5)]);

        Assert.Equal(4.25, One(result, "FRA").Exam);
    }

    [Fact]
    public void Kanti_VorschlagsnoteIsTheLastYear_AndExamFreeSubjectsKeepIt()
    {
        var result = FinalGrades.Compute(FinalMethod.KantiMatur,
            [Report("Schuljahr 2024/25", ("BIO", 5)), Report("Schuljahr 2025/26", ("BIO", 3.5))], []);

        var bio = One(result, "BIO");
        Assert.Equal(3.5, bio.Base);
        Assert.Null(bio.Exam);
        Assert.Equal(3.5, bio.Final);
        Assert.Equal("Zeugnis Schuljahr 2025/26", bio.BaseSource);
    }

    [Fact]
    public void Kanti_GeografieUsesTheLastTwoYears()
    {
        // Table: 3rd class 4, 4th class 3.5 → mean 3.75 → 4
        var result = FinalGrades.Compute(FinalMethod.KantiMatur,
            [Report("2. Klasse", ("GEO", 6)), Report("3. Klasse", ("GEO", 4)), Report("4. Klasse", ("GEO", 3.5))], []);

        Assert.Equal(4.0, One(result, "GEO").Final);
    }

    [Fact]
    public void Kanti_WholeExampleTable_Passes()
    {
        var reports = new[]
        {
            Report("4. Klasse",
                ("DEU", 5), ("FRA", 4), ("ENG", 4.5), ("MAT", 4.5), ("BIO", 3.5), ("CHE", 4.5), ("PHY", 3),
                ("GSC", 4), ("BIG", 5), ("SF", 5), ("EF", 4), ("MA", 5))
        };
        var geo = new[] { Report("3. Klasse", ("GEO", 4)), Report("4. Klasse b", ("GEO", 3.5)) };
        var exams = new[]
        {
            Exam("DEU", 4.5), Exam("FRA", 4, 4.5), Exam("ENG", 5), Exam("MAT", 4.5, 5), Exam("SF", 5, 5.5), Exam("EF", 4.5)
        };

        var finals = FinalGrades.Compute(FinalMethod.KantiMatur, [.. geo, .. reports], exams);
        var standings = FinalGrades.AsStandings(finals);

        Assert.Equal(13, standings.Count);
        Assert.Equal(3.5, Grading.PlusPoints(standings));               // «Saldo 3.5»
        Assert.Equal(ConditionStatus.Met, FinalGrades.RuleFor(FinalMethod.KantiMatur).Evaluate(standings).Status);
    }

    // ── IMS Aarau ───────────────────────────────────────────────────────

    private static IReadOnlyList<ReportCard> ImsSemesters()
    {
        // Rows 9 to 16 of the sheet, one report card per semester
        var rows = new (string Subject, double?[] Grades)[]
        {
            ("DEU", [4.5, 4.5, 4.5, 5, 4, 5]),
            ("FRA", [3, 4, 4, 5, 4, 3.5]),
            ("ENG", [5.5, 6, 4.5, 4, 5, 5.5]),
            ("MAT", [3, 4, 5, 6, 5, 5]),
            ("FRW", [4, 3, 4, 3.5, 3, 4.5]),
            ("WR",  [4, 5, 6, 4.5, 2.5, 3.5]),
            ("GP",  [null, null, 4, 4, 4, 4]),
            ("TU",  [4, 4.5, null, null, null, null]),
        };
        return Enumerable.Range(0, 6)
            .Select(i => new ReportCard($"{i + 1}. Semester",
                rows.Where(r => r.Grades[i] is not null).Select(r => S(r.Subject, r.Grades[i]!.Value)).ToList()))
            .ToList();
    }

    private static readonly ExamEntry[] ImsExams =
    [
        Exam("DEU", 4, 3.5), Exam("FRA", 3.5, 3.5), Exam("ENG", 4.5, 5.5),
        Exam("MAT", 3.5), Exam("FRW", 3.5), Exam("WR", 4),
        Exam("IDPA", null, null, own: 5.15)       // IDPA/IDAF special case: mean of 5.5 and 4.8
    ];

    [Theory]
    //               Erfahrungsnote, Prüfungsnote, Fachnote: columns K, L, M of the sheet
    [InlineData("DEU", 4.6, 3.8,  4.0)]
    [InlineData("FRA", 3.9, 3.5,  3.5)]
    [InlineData("ENG", 5.1, 5.0,  5.0)]
    [InlineData("MAT", 4.7, 3.5,  4.0)]
    [InlineData("FRW", 3.7, 3.5,  3.5)]
    [InlineData("WR",  4.3, 4.0,  4.0)]
    [InlineData("GP",  4.0, null, 4.0)]
    [InlineData("TU",  4.3, null, 4.5)]
    public void Ims_RowsOfTheGradeCalculator(string subject, double erfahrung, double? pruefung, double fachnote)
    {
        var s = One(FinalGrades.Compute(FinalMethod.ImsBm, ImsSemesters(), ImsExams), subject);

        Assert.Equal(erfahrung, s.Base!.Value, 6);
        if (pruefung is null) Assert.Null(s.Exam); else Assert.Equal(pruefung.Value, s.Exam!.Value, 6);
        Assert.Equal(fachnote, s.Final);
    }

    [Fact]
    public void Ims_WholeSheet_GesamtnoteAndMinusPoints()
    {
        var finals = FinalGrades.Compute(FinalMethod.ImsBm, ImsSemesters(), ImsExams);
        var standings = FinalGrades.AsStandings(finals);
        var result = new MinOverallAverage(4).Evaluate(standings);

        Assert.Equal(5.0, One(finals, "IDPA").Final);                   // M17
        Assert.Equal("4.2", result.Detail);                             // «Gesamtnote 4.2»
        Assert.Equal("1 Minuspunkte", new MaxDeficitPoints(2).Evaluate(standings).Detail);
    }

    // ── Target, validity, storage ───────────────────────────────────────

    [Fact]
    public void NeededExam_Kanti()
    {
        // V 4.5, target 5: mean must reach 4.75, so the exam needs 5.0
        Assert.Equal(5.0, FinalGrades.NeededExam(FinalMethod.KantiMatur, 4.5, 5), 6);
    }

    [Fact]
    public void NeededExam_Ims_AllowsForTheTenthRounding()
    {
        // E 4.6, target 4: mean must reach 3.75 → exam ≥ 2.9, and 2.85 already rounds to 2.9
        var needed = FinalGrades.NeededExam(FinalMethod.ImsBm, 4.6, 4);
        Assert.True(needed <= 2.85 + 1e-6 && needed > 2.8);
    }

    [Theory]
    [InlineData(4.5, true)]
    [InlineData(5, true)]
    [InlineData(4.25, false)]
    [InlineData(6.5, false)]
    [InlineData(0.5, false)]
    public void ExamGrades_AreWholeOrHalf(double value, bool valid)
        => Assert.Equal(valid, FinalGrades.IsValidExamGrade(value));

    [Fact]
    public void SubjectWithoutAnyReport_IsIncomplete()
    {
        var result = FinalGrades.Compute(FinalMethod.KantiMatur, [], [Exam("MA", 5)]);

        Assert.False(One(result, "MA").IsComplete);
        Assert.Empty(FinalGrades.AsStandings(result));
    }

    [Fact]
    public void State_RoundTripsThroughJson()
    {
        var state = new FinalExamState(FinalMethod.ImsBm, [Exam("DEU", 4, 3.5), Exam("IDPA", null, null, 5.15)]);

        var json = JsonSerializer.Serialize(state);
        var back = JsonSerializer.Deserialize<FinalExamState>(json)!;

        Assert.Equal(state.Method, back.Method);
        Assert.Equal(state.Exams, back.Exams);
        Assert.Contains("\"ImsBm\"", json);
    }
}
