namespace SchulnetzSync.Core.Grades;

/// <summary>One grade the user typed in by hand. Local only, nothing comes from the feed.</summary>
/// <param name="Id">Stable identity, used for editing and deleting.</param>
/// <param name="Subject">Subject code as in the timetable, e.g. "INF" or "DEU".</param>
/// <param name="Value">The grade, Swiss scale 1 to 6.</param>
/// <param name="Weight">Weight within the subject; 1 is a normal exam, 0.5 a half one.</param>
/// <param name="Title">What it was for, e.g. "Prüfung 1.1". May be empty.</param>
/// <param name="Date">When it was written.</param>
public sealed record GradeEntry(
    Guid     Id,
    string   Subject,
    double   Value,
    double   Weight,
    string   Title,
    DateOnly Date)
{
    public const double Min = 1.0;
    public const double Max = 6.0;

    /// <summary>The line between genügend and ungenügend.</summary>
    public const double Sufficient = 4.0;

    /// <summary>True when value and weight are usable.</summary>
    public bool IsValid => Value is >= Min and <= Max && Weight > 0 && !string.IsNullOrWhiteSpace(Subject);
}

/// <summary>Where one subject stands.</summary>
/// <param name="Subject">Subject code.</param>
/// <param name="Average">Exact weighted average of all grades.</param>
/// <param name="Grade">The average rounded to half grades, i.e. what goes into the report.</param>
/// <param name="Count">Number of grades.</param>
/// <param name="TotalWeight">Sum of all weights, needed for the target calculation.</param>
public sealed record SubjectStanding(
    string Subject,
    double Average,
    double Grade,
    int    Count,
    double TotalWeight)
{
    public bool IsInsufficient => Grade < GradeEntry.Sufficient;

    /// <summary>
    /// Plus points of this subject: every half grade above 4 counts +0.5, every half grade
    /// below 4 counts −1. So 5 → +1, 4.5 → +0.5, 4 → 0, 3.5 → −1, 3 → −2.
    /// </summary>
    public double PlusPoints => Grade >= GradeEntry.Sufficient
        ? Grade - GradeEntry.Sufficient
        : 2 * (Grade - GradeEntry.Sufficient);
}
