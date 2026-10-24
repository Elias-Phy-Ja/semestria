using System.Text.Json.Serialization;

namespace SchulnetzSync.Core.Grades;

/// <summary>Whether a period is half a school year or a whole one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PeriodKind
{
    /// <summary>Half a school year, with its own report (IMS, most vocational schools).</summary>
    Semester,

    /// <summary>A whole school year with one report (Kanti).</summary>
    Year
}

/// <summary>
/// A stretch of school with its own grades: «1. Semester 2026/27» or «Schuljahr 2026/27».
/// Starting a new one gives an empty grade book; the old one stays and can be looked at.
/// </summary>
/// <param name="Id">Stable identity, referenced by <see cref="GradeEntry.PeriodId"/>.</param>
/// <param name="Name">Shown in the period picker.</param>
/// <param name="Kind">Semester or school year.</param>
/// <param name="StartedAt">When it was started; orders the periods.</param>
public sealed record GradePeriod(Guid Id, string Name, PeriodKind Kind, DateTimeOffset StartedAt)
{
    public const int MaxNameLength = 40;
}

/// <summary>All periods and the one new grades go into.</summary>
/// <param name="Periods">Every period, oldest first.</param>
/// <param name="CurrentId">The running period.</param>
public sealed record GradePeriodState(IReadOnlyList<GradePeriod> Periods, Guid CurrentId)
{
    [JsonIgnore]
    public GradePeriod Current => Periods.First(p => p.Id == CurrentId);
}

/// <summary>Pure helpers around periods: names, migration of old grades, starting a new one.</summary>
public static class GradePeriods
{
    /// <summary>A Swiss school year starts in August.</summary>
    private const int SchoolYearStartMonth = 8;

    /// <summary>
    /// A name for a period starting now: August to January is the first semester of the
    /// school year, February to July the second. «1. Semester 2026/27», «Schuljahr 2026/27».
    /// </summary>
    public static string SuggestName(PeriodKind kind, DateTimeOffset now)
    {
        int startYear = now.Month >= SchoolYearStartMonth ? now.Year : now.Year - 1;
        var schoolYear = $"{startYear}/{(startYear + 1) % 100:00}";

        if (kind == PeriodKind.Year) return $"Schuljahr {schoolYear}";

        bool firstHalf = now.Month >= SchoolYearStartMonth || now.Month == 1;
        return $"{(firstHalf ? 1 : 2)}. Semester {schoolYear}";
    }

    /// <summary>
    /// The name for the period that follows: the suggestion for now, or, when a period of
    /// that name already exists (starting the next semester while the current one is still
    /// running), the next semester or school year that is still free.
    /// </summary>
    public static string SuggestNext(GradePeriodState? state, PeriodKind kind, DateTimeOffset now)
    {
        var taken = (state?.Periods ?? []).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var step  = kind == PeriodKind.Semester ? 6 : 12;

        var date = now;
        for (int i = 0; i < 8; i++, date = date.AddMonths(step))
        {
            var name = SuggestName(kind, date);
            if (!taken.Contains(name)) return name;
        }
        return SuggestName(kind, now);
    }

    /// <summary>
    /// Brings data from before periods into shape: with no period yet, one is created for
    /// now and every grade without a period goes into it. Grades pointing at a period that
    /// no longer exists go to the current one. Idempotent.
    /// </summary>
    public static (GradePeriodState State, IReadOnlyList<GradeEntry> Grades) Migrate(
        GradePeriodState? state, IReadOnlyList<GradeEntry> grades, DateTimeOffset now)
    {
        state ??= Start(null, PeriodKind.Semester, SuggestName(PeriodKind.Semester, now), now);
        if (state.Periods.All(p => p.Id != state.CurrentId))
            state = state with { CurrentId = state.Periods[^1].Id };

        var known = state.Periods.Select(p => p.Id).ToHashSet();
        var fixedGrades = grades
            .Select(g => g.PeriodId is Guid id && known.Contains(id) ? g : g with { PeriodId = state.CurrentId })
            .ToList();
        return (state, fixedGrades.AsReadOnly());
    }

    /// <summary>Starts a new period and makes it the current one. The old ones stay.</summary>
    public static GradePeriodState Start(GradePeriodState? state, PeriodKind kind, string name, DateTimeOffset now)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length == 0) clean = SuggestName(kind, now);
        if (clean.Length > GradePeriod.MaxNameLength) clean = clean[..GradePeriod.MaxNameLength].Trim();

        var period = new GradePeriod(Guid.NewGuid(), clean, kind, now);
        return new GradePeriodState([.. state?.Periods ?? [], period], period.Id);
    }

    /// <summary>The grades of one period.</summary>
    public static IEnumerable<GradeEntry> In(this IEnumerable<GradeEntry> grades, Guid periodId)
        => grades.Where(g => g.PeriodId == periodId);
}
