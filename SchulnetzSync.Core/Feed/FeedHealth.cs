namespace SchulnetzSync.Core.Feed;

/// <summary>
/// Outcome of the feed plausibility check. A feed with problems must never trigger
/// automatic deletions — half a download looks exactly like a pile of cancelled exams.
/// </summary>
public sealed record FeedHealth(IReadOnlyList<string> Problems)
{
    /// <summary>True when nothing looked wrong.</summary>
    public bool IsHealthy => Problems.Count == 0;

    /// <summary>Shared instance for the common case.</summary>
    public static FeedHealth Healthy { get; } = new(Array.Empty<string>());
}
