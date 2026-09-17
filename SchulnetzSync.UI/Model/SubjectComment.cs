namespace SchulnetzSync.UI.Model;

/// <summary>
/// A free-text note attached to a whole subject, e.g. "TEU".
///
/// Rein lokal wie die Aufgaben. Der Kommentar hängt am Fachkürzel, nicht an
/// einer einzelnen Lektion: So erscheint er bei jeder TEU-Stunde und jeder
/// TEU-Prüfung, auch wenn die Schule Lektionen verschiebt.
/// </summary>
/// <param name="Id">Stable identity for editing and deleting.</param>
/// <param name="Subject">Upper-case subject code, see <c>SubjectCode.FromSummary</c>.</param>
/// <param name="Text">The note itself, trimmed and never empty.</param>
/// <param name="CreatedAt">When the note was written.</param>
/// <param name="EditedAt">When it was last changed, null if never.</param>
public sealed record SubjectComment(
    Guid            Id,
    string          Subject,
    string          Text,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? EditedAt)
{
    /// <summary>Upper bound for one note; longer input is cut off.</summary>
    public const int MaxLength = 2000;
}
