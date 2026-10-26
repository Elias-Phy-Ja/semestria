using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Feed;

/// <summary>A lesson the app has seen in the feed, and whether it has since been cancelled.</summary>
/// <param name="Lesson">The lesson as it was last seen in the feed.</param>
/// <param name="IsCancelled">
/// True once the lesson vanished from the feed before it started. Only ever shown in the
/// in-app calendar, never written to Outlook.
/// </param>
public sealed record KnownLesson(SchulnetzEvent Lesson, bool IsCancelled);

/// <summary>
/// Works out which lessons were cancelled.
///
/// The feed has no marker for a cancelled lesson; Schulnetz simply drops it. So the only
/// way to show one is to remember the timetable from the last refresh and compare: a lesson
/// that was there, has not started yet and is now gone, was cancelled.
///
/// A pure function like <see cref="Sync.SyncEngine"/>: snapshot plus feed plus time in,
/// new snapshot out. Only call it with a healthy feed; a truncated download would
/// otherwise read like a week of cancellations.
/// </summary>
public static class LessonTracker
{
    /// <summary>
    /// When more than this share of the known upcoming lessons vanishes in one go, it is a
    /// new timetable (semester change, class change, another feed URL), not a wave of
    /// cancellations. The old lessons are then dropped without a mark.
    /// </summary>
    public const double TimetableChangeShare = 0.5;

    /// <summary>
    /// Below this many vanished lessons the share rule does not apply, so a short week with
    /// few known lessons left can still show its cancellations.
    /// </summary>
    public const int TimetableChangeMinimum = 10;

    /// <summary>How long a cancelled lesson stays in the snapshot after it would have ended.</summary>
    public static readonly TimeSpan CancelledRetention = TimeSpan.FromDays(120);

    /// <summary>Builds the new snapshot.</summary>
    /// <param name="previous">Snapshot from the last refresh; empty on the very first one.</param>
    /// <param name="feed">Every event of the current feed. Non-lessons are ignored.</param>
    /// <param name="now">Current time, passed in so the rules stay testable.</param>
    public static IReadOnlyList<KnownLesson> Update(
        IReadOnlyList<KnownLesson> previous,
        IReadOnlyList<SchulnetzEvent> feed,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(feed);

        var current = feed
            .Where(e => e.Type == SchulnetzEventType.Lektion && !e.IsAllDay)
            .ToList();

        var currentKeys  = current.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        // A lesson that only changed rooms gets a new key (the room is part of the UID), but
        // keeps its slot. Something still in that slot means it was moved, not cancelled.
        var currentSlots = current.Select(e => (e.Start, e.End)).ToHashSet();

        var result = current.Select(e => new KnownLesson(e, IsCancelled: false)).ToList();

        var vanished = previous
            .Where(k => !k.IsCancelled
                     && !currentKeys.Contains(k.Lesson.Key)
                     && k.Lesson.Start > now
                     && !currentSlots.Contains((k.Lesson.Start, k.Lesson.End)))
            .ToList();

        int knownUpcoming = previous.Count(k => !k.IsCancelled && k.Lesson.Start > now);
        bool isNewTimetable = current.Count == 0
            || (vanished.Count >= TimetableChangeMinimum
                && vanished.Count > knownUpcoming * TimetableChangeShare);

        if (!isNewTimetable)
            result.AddRange(vanished.Select(k => k with { IsCancelled = true }));

        // Cancellations found earlier stay, unless the lesson came back or is long past.
        result.AddRange(previous.Where(k => k.IsCancelled
                                         && !currentKeys.Contains(k.Lesson.Key)
                                         && !currentSlots.Contains((k.Lesson.Start, k.Lesson.End))
                                         && k.Lesson.End > now - CancelledRetention));

        return result
            .GroupBy(k => k.Lesson.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(k => k.Lesson.Start)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Starting point for the very first run of a version with this feature: the lessons of
    /// the cached feed from the last session, none of them cancelled.
    /// </summary>
    public static IReadOnlyList<KnownLesson> Seed(IReadOnlyList<SchulnetzEvent> cachedFeed)
        => cachedFeed
            .Where(e => e.Type == SchulnetzEventType.Lektion && !e.IsAllDay)
            .Select(e => new KnownLesson(e, IsCancelled: false))
            .ToList()
            .AsReadOnly();
}
