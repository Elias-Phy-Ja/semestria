namespace SchulnetzSync.Core.Feed;

/// <summary>
/// Abstraction over any source that delivers raw iCal content.
/// Implementations: <see cref="HttpFeedSource"/> (production), in-memory fakes (tests).
/// </summary>
public interface IFeedSource
{
    /// <summary>Returns the raw iCal text of the feed.</summary>
    Task<string> FetchAsync(CancellationToken cancellationToken = default);
}
