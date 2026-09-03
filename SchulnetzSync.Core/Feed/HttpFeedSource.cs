namespace SchulnetzSync.Core.Feed;

/// <summary>
/// Downloads the iCal feed over HTTPS.
/// The feed URL is treated as a secret: the query string is never included
/// in log messages or exception texts.
/// </summary>
public sealed class HttpFeedSource : IFeedSource
{
    private const int TimeoutSeconds = 30;

    private readonly HttpClient _httpClient;
    private readonly Uri _feedUri;

    /// <summary>
    /// Path-only representation of the URL, safe to include in logs
    /// (query string with personal token stripped).
    /// </summary>
    private readonly string _safeUriForLogging;

    /// <param name="httpClient">Caller-owned HttpClient (manage lifetime externally).</param>
    /// <param name="feedUrl">
    /// Full feed URL including token. May use the <c>webcal://</c> scheme —
    /// it is silently rewritten to <c>https://</c>.
    /// </param>
    public HttpFeedSource(HttpClient httpClient, string feedUrl)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);

        // webcal:// is the same as https:// but used by calendar apps for subscription links.
        var normalised = feedUrl.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
            ? string.Concat("https://", feedUrl.AsSpan(9))
            : feedUrl;

        _feedUri = new Uri(normalised, UriKind.Absolute);
        _httpClient = httpClient;

        // Strip query string so the token never leaks into logs or exceptions.
        _safeUriForLogging = _feedUri.GetLeftPart(UriPartial.Path);
    }

    /// <inheritdoc/>
    public async Task<string> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            return await _httpClient.GetStringAsync(_feedUri, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The linked CTS fired (our timeout), not the caller's token.
            throw new TimeoutException(
                $"Feed request timed out after {TimeoutSeconds} s. " +
                $"URL (path, token omitted): {_safeUriForLogging}");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to fetch feed from {_safeUriForLogging}: {ex.Message}", ex);
        }
    }
}
