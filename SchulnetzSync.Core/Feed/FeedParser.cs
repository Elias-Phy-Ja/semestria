using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using SchulnetzSync.Core.Model;
using IcalCalendar = Ical.Net.Calendar;

namespace SchulnetzSync.Core.Feed;

/// <summary>
/// Parses raw iCal text from the Schulnetz feed into typed <see cref="SchulnetzEvent"/> objects.
/// Classification is driven exclusively by the UID prefix — never by SUMMARY content.
/// </summary>
public static class FeedParser
{
    // Zurich offset for all events; DST-aware via GetUtcOffset per date.
    private static readonly TimeZoneInfo s_zurichTz =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");

    // UID format: <date>et<id>et<start>et<end>et<room>@centerboard.ch
    // The second segment (index 1) after splitting on "et" is the type discriminator.
    private const string UidSegmentSeparator = "et";

    /// <summary>
    /// Parses all VEVENTs from the given iCal text into a flat list.
    /// Events with an unrecognisable or missing UID are silently skipped.
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

            // DtStart is nullable in Ical.Net 5.x; an event without a start time is invalid.
            if (calEvent.DtStart is null)
                continue;

            var (key, type) = ClassifyUid(calEvent.Uid);

            bool isAllDay = !calEvent.DtStart.HasTime;
            DateTimeOffset start = ToOffset(calEvent.DtStart);
            DateTimeOffset end = ResolveEnd(calEvent, start);

            // Empty LOCATION becomes null — never an empty string.
            string? location = string.IsNullOrEmpty(calEvent.Location)
                ? null
                : calEvent.Location;

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
    /// Runs a quick structural check on the raw iCal text.
    /// Returns a <see cref="FeedHealth"/> describing any problems found.
    /// An unhealthy feed must not trigger automatic delete operations.
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

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Determines the event type and stable correlation key from the UID.
    ///
    /// UID structure (Centerboard):
    ///   20260907 et P_65100 et 14:55 et 15:40 et Pruefung @centerboard.ch
    ///   segment 0    segment 1   ...                        (split on "et")
    ///
    /// The second segment (index 1) carries the type:
    ///   P_…   → Pruefung   — key = that segment ("P_65100")
    ///   T_…   → Termin     — key = that segment ("T_7409")
    ///   other → Lektion    — key = everything before "@" (date+id+times+room,
    ///                         because lesson series IDs repeat across dates)
    /// </summary>
    private static (string Key, SchulnetzEventType Type) ClassifyUid(string uid)
    {
        // Strip domain part: take everything before the first '@'.
        ReadOnlySpan<char> beforeAt = uid.AsSpan();
        int atIndex = uid.IndexOf('@');
        if (atIndex > 0)
            beforeAt = beforeAt[..atIndex];

        string beforeAtStr = beforeAt.ToString();

        // Split on "et" — the Centerboard field separator.
        string[] segments = beforeAtStr.Split(UidSegmentSeparator);

        if (segments.Length < 2)
            return (beforeAtStr, SchulnetzEventType.Lektion);

        string discriminator = segments[1];

        if (discriminator.StartsWith("P_", StringComparison.Ordinal))
            return (discriminator, SchulnetzEventType.Pruefung);

        if (discriminator.StartsWith("T_", StringComparison.Ordinal))
            return (discriminator, SchulnetzEventType.Termin);

        // Numeric → Lektion. Key is the full before-@ string so that two
        // lessons of the same series on different dates get distinct keys.
        return (beforeAtStr, SchulnetzEventType.Lektion);
    }

    /// <summary>
    /// Converts an Ical.Net <see cref="CalDateTime"/> to a <see cref="DateTimeOffset"/>
    /// using the Europe/Zurich timezone.
    ///
    /// For all-day events (VALUE=DATE) the time component is midnight; the
    /// Zurich offset is still applied so the value is unambiguous.
    /// </summary>
    private static DateTimeOffset ToOffset(CalDateTime calDt)
    {
        if (calDt.IsUtc)
            return new DateTimeOffset(calDt.Value, TimeSpan.Zero);

        // calDt.Value is the local Zurich DateTime; force Unspecified kind so
        // GetUtcOffset treats it as the named timezone, not system-local.
        var dt = DateTime.SpecifyKind(calDt.Value, DateTimeKind.Unspecified);
        TimeSpan offset = s_zurichTz.GetUtcOffset(dt);
        return new DateTimeOffset(dt, offset);
    }

    /// <summary>
    /// Determines the event end time.
    ///
    /// The feed uses two patterns:
    ///   1. Timed events: DTEND present.
    ///   2. All-day events: DURATION present, DTEND absent.
    ///      End = Start + Duration (e.g. DURATION:P4D → four-day span).
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

        // Zero-duration event (should not appear in this feed, but handle gracefully).
        return start;
    }
}
