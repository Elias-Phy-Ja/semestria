namespace SchulnetzSync.Core.Feed;

/// <summary>
/// Downloads the iCal feed over HTTPS. The URL is a secret because it carries a personal
/// token, so the query string never makes it into a log line or an exception message.
/// </summary>
public sealed class HttpFeedSource : IFeedSource
{
    private const int TimeoutSeconds = 30;

    private readonly HttpClient _httpClient;
    private readonly Uri _feedUri;

    /// <summary>URL without its query string — the only form that may show up anywhere.</summary>
    private readonly string _safeUriForLogging;

    /// <param name="httpClient">Owned by the caller, lifetime managed outside.</param>
    /// <param name="feedUrl">Full feed URL including the token. <c>webcal://</c> is accepted.</param>
    public HttpFeedSource(HttpClient httpClient, string feedUrl)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);

        // webcal:// is plain https underneath; calendar apps only use it for subscribe links.
        var normalised = feedUrl.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
            ? string.Concat("https://", feedUrl.AsSpan(9))
            : feedUrl;

        _feedUri = new Uri(normalised, UriKind.Absolute);
        _httpClient = httpClient;

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
            // Our own timeout fired, not the caller's token — say so instead of reporting a cancel.
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
