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

    /// <summary>
    /// Splits a summary into subject code and the rest, e.g.
    /// "FRA_I26A_DuaLi Examen de grammaire" into ("FRA", "Examen de grammaire").
    ///
    /// Anders als <see cref="FromSummary"/> rät die Methode nicht: Nur wenn der
    /// erste Teil dem Schulnetz-Muster FACH_KLASSE_LEHRER folgt, gibt es einen
    /// Code. Titel wie "Begrüssung und Einführung 1. Klasse" bleiben ganz stehen.
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
