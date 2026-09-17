using System.Text.RegularExpressions;

namespace SchulnetzSync.Core.Model;

/// <summary>
/// Derives the subject code ("TEU", "FRA") from a Schulnetz summary.
///
/// Der Code ist der Schlüssel für Fachfarben, Aufgabenlisten-Vorschläge und
/// Fachkommentare. Er muss daher für Lektionen und Prüfungen desselben Fachs
/// gleich ausfallen: «9:30 TEU_I26A» und «TEU_I26A Prüfung 1.1» ergeben beide «TEU».
/// </summary>
public static partial class SubjectCode
{
    /// <summary>Longest code taken from a summary without an underscore.</summary>
    private const int MaxLengthWithoutUnderscore = 6;

    /// <summary>
    /// Returns the upper-case subject code. Everything before the first underscore;
    /// without underscore the first six characters. A leading time ("9:30 ") is ignored.
    /// Returns an empty string for an empty summary.
    /// </summary>
    public static string FromSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return "";

        var s    = LeadingTime().Replace(summary.Trim(), "");
        var idx  = s.IndexOf('_');
        var code = idx > 0 ? s[..idx] : s[..Math.Min(s.Length, MaxLengthWithoutUnderscore)];
        return code.Trim().ToUpperInvariant();
    }

    [GeneratedRegex(@"^\d{1,2}:\d{2}\s+")]
    private static partial Regex LeadingTime();
}
