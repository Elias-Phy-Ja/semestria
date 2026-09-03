namespace SchulnetzSync.Core.Feed;

/// <summary>
/// Result of a feed plausibility check.
/// A feed with problems must not trigger automatic delete operations.
/// </summary>
public sealed record FeedHealth(IReadOnlyList<string> Problems)
{
    /// <summary>True when no problems were found.</summary>
    public bool IsHealthy => Problems.Count == 0;

    /// <summary>A pre-built healthy instance with zero problems.</summary>
    public static FeedHealth Healthy { get; } = new(Array.Empty<string>());
}
