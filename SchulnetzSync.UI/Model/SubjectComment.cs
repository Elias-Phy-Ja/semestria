namespace SchulnetzSync.UI.Model;

/// <summary>
/// A free-text note attached to a whole subject, e.g. "TEU".
///
/// Local only, same as the tasks. The note hangs off the subject code rather than off one
/// lesson, so it shows up at every TEU lesson and every TEU exam — even when the school
/// moves lessons around.
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
