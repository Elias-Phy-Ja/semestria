namespace SchulnetzSync.Core.Model;

/// <summary>
/// The user's changes to an exam from the feed. Kept apart from the feed data, because the
/// feed may deliver the exam again at any time and must not wipe them, and the other way
/// round a change must not hide what the school decides later.
/// </summary>
/// <param name="Key">Inner id of the exam, e.g. "P_65100".</param>
/// <param name="Title">New title, null when unchanged.</param>
/// <param name="Location">New room, null when unchanged, empty when removed.</param>
/// <param name="Start">New start, null when the time is unchanged.</param>
/// <param name="End">New end, null when the time is unchanged.</param>
/// <param name="BaseTitle">Title in the feed when the change was made.</param>
/// <param name="BaseLocation">Room in the feed when the change was made.</param>
/// <param name="BaseStart">Start in the feed when the change was made.</param>
/// <param name="BaseEnd">End in the feed when the change was made.</param>
public sealed record PruefungOverlay(
    string          Key,
    string?         Title,
    string?         Location,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string          BaseTitle,
    string?         BaseLocation,
    DateTimeOffset  BaseStart,
    DateTimeOffset  BaseEnd)
{
    /// <summary>True when no change is left in it.</summary>
    public bool IsEmpty => Title is null && Location is null && Start is null && End is null;
}

/// <summary>Which part of an exam the school changed after the user did.</summary>
public enum OverlayField { Title, Location, Time }

/// <summary>A change of the user that gave way to a newer one from the school.</summary>
/// <param name="Key">Inner id of the exam.</param>
/// <param name="Field">The part concerned.</param>
public sealed record OverlayNotice(string Key, OverlayField Field);

/// <summary>The feed with the user's changes laid over it.</summary>
/// <param name="Events">All events, exams with their changes applied.</param>
/// <param name="Overlays">The overlays that remain; store these.</param>
/// <param name="Notices">Changes that gave way to the feed, to be shown to the user.</param>
public sealed record OverlayResult(
    IReadOnlyList<SchulnetzEvent>  Events,
    IReadOnlyList<PruefungOverlay> Overlays,
    IReadOnlyList<OverlayNotice>   Notices);

/// <summary>
/// Lays the user's changes over the feed. Pure function.
///
/// When the school later changes a field the user changed too, the school wins, because
/// nobody should study for a date that is no longer true. The change is dropped and a
/// notice tells the user.
/// </summary>
public static class PruefungOverlays
{
    public static OverlayResult Apply(
        IReadOnlyList<SchulnetzEvent>  feed,
        IReadOnlyList<PruefungOverlay> overlays)
    {
        var byKey   = overlays.ToDictionary(o => o.Key, StringComparer.Ordinal);
        var kept    = new Dictionary<string, PruefungOverlay>(byKey, StringComparer.Ordinal);
        var notices = new List<OverlayNotice>();

        var events = feed.Select(ev =>
        {
            if (ev.Type != SchulnetzEventType.Pruefung || !byKey.TryGetValue(ev.Key, out var overlay))
                return ev;

            var (shown, left) = Merge(ev, overlay, notices);
            if (left.IsEmpty) kept.Remove(ev.Key); else kept[ev.Key] = left;
            return shown;
        }).ToList();

        // An overlay without a matching exam stays: the exam may only be missing from this feed.
        return new OverlayResult(events, kept.Values.OrderBy(o => o.Key, StringComparer.Ordinal).ToList(), notices);
    }

    private static (SchulnetzEvent Shown, PruefungOverlay Left) Merge(
        SchulnetzEvent ev, PruefungOverlay overlay, List<OverlayNotice> notices)
    {
        var shown = ev;
        var left  = overlay;

        if (overlay.Title is { } title)
        {
            if (ev.Summary == overlay.BaseTitle) shown = shown with { Summary = title };
            else
            {
                if (ev.Summary != title) notices.Add(new OverlayNotice(ev.Key, OverlayField.Title));
                left = left with { Title = null };
            }
        }

        if (overlay.Location is { } location)
        {
            if (ev.Location == overlay.BaseLocation) shown = shown with { Location = location.Length == 0 ? null : location };
            else
            {
                if ((ev.Location ?? string.Empty) != location) notices.Add(new OverlayNotice(ev.Key, OverlayField.Location));
                left = left with { Location = null };
            }
        }

        if (overlay.Start is not null || overlay.End is not null)
        {
            var start = overlay.Start ?? overlay.BaseStart;
            var end   = overlay.End   ?? overlay.BaseEnd;

            if (ev.Start == overlay.BaseStart && ev.End == overlay.BaseEnd) shown = shown with { Start = start, End = end };
            else
            {
                if (ev.Start != start || ev.End != end) notices.Add(new OverlayNotice(ev.Key, OverlayField.Time));
                left = left with { Start = null, End = null };
            }
        }

        return (shown, left);
    }
}
