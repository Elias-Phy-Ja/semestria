using System.Text.RegularExpressions;

namespace SchulnetzSync.Core.Model;

/// <summary>
/// Pulls the subject code ("TEU", "FRA") out of a Schulnetz summary.
///
/// The code is the key for subject colours, task suggestions and subject comments, so a
/// lesson and an exam of the same subject have to end up with the same one: "9:30 TEU_I26A"
/// and "TEU_I26A Prüfung 1.1" both have to give "TEU".
/// </summary>
public static partial class SubjectCode
{
    /// <summary>Longest code we accept from a summary that has no underscore.</summary>
    private const int MaxLengthWithoutUnderscore = 6;

    /// <summary>
    /// Upper-case subject code: everything before the first underscore, or the first six
    /// characters when there is none. A leading time ("9:30 ") is ignored. Empty in, empty out.
    /// </summary>
    public static string FromSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return "";

        var s    = LeadingTime().Replace(summary.Trim(), "");
        var idx  = s.IndexOf('_');
        var code = idx > 0 ? s[..idx] : s[..Math.Min(s.Length, MaxLengthWithoutUnderscore)];
        return code.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Splits a summary into code and remainder, e.g. "FRA_I26A_DuaLi Examen de grammaire"
    /// into ("FRA", "Examen de grammaire").
    ///
    /// Unlike <see cref="FromSummary"/> this one does not guess: there is only a code when
    /// the first word follows the Schulnetz pattern SUBJECT_CLASS_TEACHER. Titles such as
    /// "Begrüssung und Einführung 1. Klasse" stay in one piece.
    /// </summary>
    public static (string Code, string Detail) Split(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return ("", "");

        var s     = LeadingTime().Replace(summary.Trim(), "");
        var space = s.IndexOf(' ');
        var first = space < 0 ? s : s[..space];

        var underscore = first.IndexOf('_');
        if (underscore <= 0) return ("", s);

        var rest = space < 0 ? "" : s[(space + 1)..].Trim();
        return (first[..underscore].ToUpperInvariant(), rest);
    }

    [GeneratedRegex(@"^\d{1,2}:\d{2}\s+")]
    private static partial Regex LeadingTime();
}
