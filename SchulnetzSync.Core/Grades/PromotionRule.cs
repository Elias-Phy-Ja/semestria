using System.Globalization;
using System.Text.Json.Serialization;

namespace SchulnetzSync.Core.Grades;

/// <summary>Outcome of one condition.</summary>
public enum ConditionStatus
{
    /// <summary>Fulfilled with the grades so far.</summary>
    Met,

    /// <summary>Not fulfilled.</summary>
    Failed,

    /// <summary>Cannot be judged yet, e.g. a required subject has no grade.</summary>
    Open
}

/// <summary>One line of the promotion check, ready to show.</summary>
/// <param name="Label">The rule in words, e.g. "Gesamtschnitt mindestens 4".</param>
/// <param name="Status">Whether it holds.</param>
/// <param name="Detail">The current value, e.g. "4.31".</param>
public sealed record ConditionResult(string Label, ConditionStatus Status, string Detail);

/// <summary>
/// A single requirement for promotion. Both built-in systems are just lists of these, and a
/// custom rule is the same thing put together by the user.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MinPlusPoints),           "minPlusPoints")]
[JsonDerivedType(typeof(MaxInsufficientSubjects), "maxInsufficient")]
[JsonDerivedType(typeof(MinOverallAverage),       "minOverallAverage")]
[JsonDerivedType(typeof(MinSubjectGrade),         "minSubjectGrade")]
public abstract record RuleCondition
{
    public abstract ConditionResult Evaluate(IReadOnlyList<SubjectStanding> standings);

    protected static string Format(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>The plus points over all subjects must reach a minimum, usually 0.</summary>
public sealed record MinPlusPoints(double Min) : RuleCondition
{
    public override ConditionResult Evaluate(IReadOnlyList<SubjectStanding> standings)
    {
        string label = $"Pluspunkte mindestens {Format(Min)}";
        if (standings.Count == 0) return new(label, ConditionStatus.Open, "noch keine Noten");

        double points = Grading.PlusPoints(standings);
        string sign   = points > 0 ? "+" : "";
        return new(label, points >= Min ? ConditionStatus.Met : ConditionStatus.Failed,
                   $"{sign}{Format(points)} Punkte");
    }
}

/// <summary>At most this many subjects may be below 4.</summary>
public sealed record MaxInsufficientSubjects(int Max) : RuleCondition
{
    public override ConditionResult Evaluate(IReadOnlyList<SubjectStanding> standings)
    {
        string label = Max == 0
            ? "Kein Fach ungenügend"
            : $"Höchstens {Max} Fächer ungenügend";
        var insufficient = standings.Where(s => s.IsInsufficient).Select(s => s.Subject).ToList();
        string detail = insufficient.Count == 0 ? "keines" : string.Join(", ", insufficient);
        return new(label, insufficient.Count <= Max ? ConditionStatus.Met : ConditionStatus.Failed, detail);
    }
}

/// <summary>The average of the report grades must reach a minimum, usually 4.</summary>
public sealed record MinOverallAverage(double Min) : RuleCondition
{
    public override ConditionResult Evaluate(IReadOnlyList<SubjectStanding> standings)
    {
        string label = $"Gesamtschnitt mindestens {Format(Min)}";
        if (Grading.OverallAverage(standings) is not double avg)
            return new(label, ConditionStatus.Open, "noch keine Noten");

        // Compare on two decimals, so 3.9999… from floating point does not fail a clean 4
        return new(label, Math.Round(avg, 2) >= Min ? ConditionStatus.Met : ConditionStatus.Failed,
                   avg.ToString("0.00", CultureInfo.InvariantCulture));
    }
}

/// <summary>A key subject, e.g. Informatik at the IMS, must reach a minimum on its own.</summary>
public sealed record MinSubjectGrade(string Subject, double Min) : RuleCondition
{
    public override ConditionResult Evaluate(IReadOnlyList<SubjectStanding> standings)
    {
        string label = $"{Subject} mindestens {Format(Min)}";
        var s = standings.FirstOrDefault(x => string.Equals(x.Subject, Subject, StringComparison.OrdinalIgnoreCase));
        if (s is null) return new(label, ConditionStatus.Open, "noch keine Note");

        return new(label, s.Grade >= Min ? ConditionStatus.Met : ConditionStatus.Failed, Format(s.Grade));
    }
}

/// <summary>A named set of conditions; promoted means every one of them holds.</summary>
public sealed record PromotionRule(string Name, IReadOnlyList<RuleCondition> Conditions)
{
    /// <summary>
    /// System 1: plus points. Every half grade above 4 gives +0.5, every half grade below
    /// costs −1; the sum must not be negative, and at most two subjects may be below 4.
    /// </summary>
    public static PromotionRule PlusPointsSystem { get; } = new("Pluspunkte",
    [
        new MinPlusPoints(0),
        new MaxInsufficientSubjects(2)
    ]);

    /// <summary>
    /// System 2: overall average at least 4 and at most two subjects below 4. Key subjects
    /// (Informatik at the IMS) are added by the user as <see cref="MinSubjectGrade"/>.
    /// </summary>
    public static PromotionRule AverageSystem { get; } = new("Gesamtschnitt",
    [
        new MinOverallAverage(4),
        new MaxInsufficientSubjects(2)
    ]);

    public PromotionResult Evaluate(IReadOnlyList<SubjectStanding> standings)
    {
        var results = Conditions.Select(c => c.Evaluate(standings)).ToList();
        var status  = results.Any(r => r.Status == ConditionStatus.Failed) ? ConditionStatus.Failed
                    : results.Any(r => r.Status == ConditionStatus.Open)   ? ConditionStatus.Open
                    : ConditionStatus.Met;
        return new PromotionResult(status, results.AsReadOnly());
    }
}

/// <summary>The whole check: overall verdict plus one line per condition.</summary>
public sealed record PromotionResult(ConditionStatus Status, IReadOnlyList<ConditionResult> Conditions);
