using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using SchulnetzSync.Core.Model;
using IcalCalendar = Ical.Net.Calendar;

namespace SchulnetzSync.Core.Feed;

/// <summary>
/// Turns the raw iCal text of the Schulnetz feed into typed <see cref="SchulnetzEvent"/>s.
/// Classification runs on the UID alone — SUMMARY is never asked, because it lies.
/// </summary>
public static class FeedParser
{
    // Everything in the feed is Zurich local time; GetUtcOffset per date handles DST.
    private static readonly TimeZoneInfo s_zurichTz =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");

    // UID layout: <date>et<id>et<start>et<end>et<room>@centerboard.ch
    private const string UidSegmentSeparator = "et";

    /// <summary>
    /// Parses every VEVENT into a flat list. Entries without a usable UID or start time
    /// are skipped rather than reported — the feed occasionally ships such leftovers.
    /// </summary>
    public static IReadOnlyList<SchulnetzEvent> Parse(string icsContent)
    {
        ArgumentNullException.ThrowIfNull(icsContent);

        var calendar = IcalCalendar.Load(icsContent);
        if (calendar is null)
            return Array.Empty<SchulnetzEvent>();

        var result = new List<SchulnetzEvent>(calendar.Events.Count);

        foreach (CalendarEvent calEvent in calendar.Events)
        {
            if (string.IsNullOrWhiteSpace(calEvent.Uid))
                continue;

            // DtStart is nullable since Ical.Net 5.x, and an event without a start is junk.
            if (calEvent.DtStart is null)
                continue;

            var (key, type) = ClassifyUid(calEvent.Uid);

            bool isAllDay = !calEvent.DtStart.HasTime;
            DateTimeOffset start = ToOffset(calEvent.DtStart);
            DateTimeOffset end = ResolveEnd(calEvent, start);

            // Empty LOCATION becomes null, never "". When it is missing the UID sometimes
            // still carries the room, so try there before giving up.
            string? location = !string.IsNullOrEmpty(calEvent.Location)
                ? calEvent.Location
                : ExtractRoomFromUid(calEvent.Uid);

            result.Add(new SchulnetzEvent(
                Key: key,
                RawUid: calEvent.Uid,
                Type: type,
                Start: start,
                End: end,
                IsAllDay: isAllDay,
                Summary: calEvent.Summary ?? string.Empty,
                Location: location));
        }

        return result.AsReadOnly();
    }
    /// <summary>
    /// Quick structural sanity check on the raw text. An unhealthy feed must not be
    /// allowed to delete anything — a truncated download reads like a mass cancellation.
    /// </summary>
    public static FeedHealth CheckPlausibility(string icsContent)
    {
        ArgumentNullException.ThrowIfNull(icsContent);

        var problems = new List<string>();

        if (!icsContent.TrimEnd().EndsWith("END:VCALENDAR", StringComparison.Ordinal))
            problems.Add("Feed does not end with END:VCALENDAR — the download may be truncated.");

        if (!icsContent.Contains("BEGIN:VEVENT", StringComparison.Ordinal))
            problems.Add("Feed contains no events (no BEGIN:VEVENT marker found).");

        return problems.Count == 0
            ? FeedHealth.Healthy
            : new FeedHealth(problems);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Last resort for a missing room: segment 4 of the UID
    /// (&lt;date&gt;et&lt;id&gt;et&lt;start&gt;et&lt;end&gt;et&lt;room&gt;@centerboard.ch).
    /// That slot also holds type keywords and times, so anything that does not look
    /// like a room name is rejected instead of ending up in the calendar.
    /// </summary>
    private static string? ExtractRoomFromUid(string uid)
    {
        int atIdx    = uid.IndexOf('@');
        var beforeAt = atIdx > 0 ? uid[..atIdx] : uid;
        var segs     = beforeAt.Split(UidSegmentSeparator);
        if (segs.Length < 5) return null;

        var candidate = segs[4].Trim();
        if (string.IsNullOrWhiteSpace(candidate))                                    return null;
        // Type keywords
        if (candidate.Equals("Pruefung",  StringComparison.OrdinalIgnoreCase))       return null;
        if (candidate.Equals("Prüfung",   StringComparison.OrdinalIgnoreCase))       return null;
        if (candidate.Equals("Termin",    StringComparison.OrdinalIgnoreCase))       return null;
        if (candidate.Equals("Lektion",   StringComparison.OrdinalIgnoreCase))       return null;
        // Times like "14:55"
        if (System.Text.RegularExpressions.Regex.IsMatch(candidate, @"^\d{1,2}:\d{2}$")) return null;
        // Bare numbers
        if (System.Text.RegularExpressions.Regex.IsMatch(candidate, @"^\d+$"))       return null;
        if (candidate.Length < 2)                                                     return null;

        return candidate;
    }
    /// <summary>
    /// Reads type and correlation key out of the UID.
    ///
    /// A Centerboard UID looks like this:
    ///   20260907 et P_65100 et 14:55 et 15:40 et Pruefung @centerboard.ch
    /// Split on "et", and segment 1 carries the type:
    ///   P_…   → Pruefung, key is that segment ("P_65100")
    ///   T_…   → Termin,   key is that segment ("T_7409")
    ///   other → Lektion,  key is everything before "@", because lesson ids repeat
    ///           week after week and would otherwise collide across dates.
    /// </summary>
    private static (string Key, SchulnetzEventType Type) ClassifyUid(string uid)
    {
        ReadOnlySpan<char> beforeAt = uid.AsSpan();
        int atIndex = uid.IndexOf('@');
        if (atIndex > 0)
            beforeAt = beforeAt[..atIndex];

        string beforeAtStr = beforeAt.ToString();

        string[] segments = beforeAtStr.Split(UidSegmentSeparator);

        if (segments.Length < 2)
            return (beforeAtStr, SchulnetzEventType.Lektion);

        string discriminator = segments[1];

        if (discriminator.StartsWith("P_", StringComparison.Ordinal))
            return (discriminator, SchulnetzEventType.Pruefung);

        if (discriminator.StartsWith("T_", StringComparison.Ordinal))
            return (discriminator, SchulnetzEventType.Termin);

        return (beforeAtStr, SchulnetzEventType.Lektion);
    }

    /// <summary>
    /// Ical.Net date to <see cref="DateTimeOffset"/> in Europe/Zurich. All-day entries
    /// land on midnight, but still get the offset so the value stays unambiguous.
    /// </summary>
    private static DateTimeOffset ToOffset(CalDateTime calDt)
    {
        if (calDt.IsUtc)
            return new DateTimeOffset(calDt.Value, TimeSpan.Zero);

        // Force Unspecified, otherwise GetUtcOffset reads it as the machine timezone
        // instead of Zurich — which is wrong as soon as the laptop travels.
        var dt = DateTime.SpecifyKind(calDt.Value, DateTimeKind.Unspecified);
        TimeSpan offset = s_zurichTz.GetUtcOffset(dt);
        return new DateTimeOffset(dt, offset);
    }

    /// <summary>
    /// Works out the end time. Timed events bring a DTEND; all-day events bring a
    /// DURATION instead (DURATION:P4D for a four-day block), so add it to the start.
    /// </summary>
    private static DateTimeOffset ResolveEnd(CalendarEvent calEvent, DateTimeOffset start)
    {
        if (calEvent.DtEnd is not null)
            return ToOffset(calEvent.DtEnd);

        if (calEvent.Duration.HasValue)
        {
            Duration dur = calEvent.Duration.Value;
            TimeSpan span =
                TimeSpan.FromDays((dur.Weeks ?? 0) * 7 + (dur.Days ?? 0))
                + TimeSpan.FromHours(dur.Hours ?? 0)
                + TimeSpan.FromMinutes(dur.Minutes ?? 0)
                + TimeSpan.FromSeconds(dur.Seconds ?? 0);
            return start + span;
        }

        // Neither field set. Should not happen in this feed, but do not crash over it.
        return start;
    }
}
