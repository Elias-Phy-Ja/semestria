namespace SchulnetzSync.Core.News;

/// <summary>Where the news come from; an interface so tests run without a network.</summary>
public interface INewsSource
{
    Task<NewsFeed> FetchAsync(CancellationToken ct = default);
}

/// <summary>
/// Downloads the public news.json. A plain GET without cookies, query string or anything
/// that identifies the user; the server learns no more than from any page visit.
/// </summary>
public sealed class HttpNewsSource : INewsSource
{
    /// <summary>The one place the address lives.</summary>
    public const string DefaultUrl = "https://semestria.elias-wyss.ch/news.json";

    private const int TimeoutSeconds = 10;

    /// <summary>A news file is a few kilobytes; anything near this is not ours.</summary>
    private const int MaxBytes = 256 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _uri;

    public HttpNewsSource(HttpClient http, string url = DefaultUrl)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _uri  = new Uri(url, UriKind.Absolute);
        if (_uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("News must come over https.", nameof(url));
    }

    public async Task<NewsFeed> FetchAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            using var response = await _http.GetAsync(_uri, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                                            .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaxBytes)
                throw new InvalidOperationException($"news.json is larger than {MaxBytes} bytes.");

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (json.Length > MaxBytes)
                throw new InvalidOperationException($"news.json is larger than {MaxBytes} bytes.");

            return NewsFeed.Parse(json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"News request timed out after {TimeoutSeconds} s.");
        }
    }
}
