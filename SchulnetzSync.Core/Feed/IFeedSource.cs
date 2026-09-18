namespace SchulnetzSync.Core.Feed;

/// <summary>
/// Anything that can hand us raw iCal text: <see cref="HttpFeedSource"/> in production,
/// in-memory fakes in the tests. That separation is the whole point of the interface.
/// </summary>
public interface IFeedSource
{
    /// <summary>Returns the raw iCal text of the feed.</summary>
    Task<string> FetchAsync(CancellationToken cancellationToken = default);
}
