using System.Text.Json;
using System.Text.Json.Serialization;
using SchulnetzSync.Core.Update;

namespace SchulnetzSync.Core.News;

/// <summary>
/// One message from the developer: an announcement, a survey link, a hint about an update.
/// Comes from the public news.json on semestria.elias-wyss.ch, edited in the admin page.
/// </summary>
/// <param name="Id">Stable, unique id. The app remembers which ids it has shown.</param>
/// <param name="Title">Headline.</param>
/// <param name="Body">Text, plain, may contain line breaks.</param>
/// <param name="Kind">"info", "survey" or "update"; only changes the icon.</param>
/// <param name="Link">Optional https link, e.g. a form for a survey.</param>
/// <param name="LinkLabel">Text of the link button; a default is used when empty.</param>
/// <param name="PublishedAt">Not shown before this time.</param>
/// <param name="ExpiresAt">Not shown after this time; null means never expires.</param>
/// <param name="MinVersion">Only for app versions from this one on; null means all.</param>
/// <param name="MaxVersion">Only for app versions up to this one; null means all.</param>
public sealed record NewsItem(
    string          Id,
    string          Title,
    string          Body,
    string          Kind,
    string?         Link,
    string?         LinkLabel,
    DateTimeOffset  PublishedAt,
    DateTimeOffset? ExpiresAt,
    string?         MinVersion,
    string?         MaxVersion)
{
    /// <summary>
    /// True when <see cref="Link"/> is an absolute https address. Anything else (file:,
    /// javascript:, a bare word) is never opened, even if it made it into the file.
    /// </summary>
    public bool HasSafeLink
        => Uri.TryCreate(Link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>The whole news.json.</summary>
/// <param name="Version">Format version of the file; this app reads version 1.</param>
/// <param name="Items">All messages, old and new.</param>
public sealed record NewsFeed(int Version, IReadOnlyList<NewsItem> Items)
{
    public const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Reads news.json. A file in a newer format, or one that does not parse, is reported
    /// as an exception — the caller logs it, the user is not bothered.
    /// </summary>
    public static NewsFeed Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var feed = JsonSerializer.Deserialize<NewsFeed>(json, s_json)
            ?? throw new FormatException("news.json is empty.");
        if (feed.Version != SupportedVersion)
            throw new FormatException($"news.json has format version {feed.Version}, expected {SupportedVersion}.");

        // Items without id or title are skipped rather than failing the whole file
        var usable = (feed.Items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Title))
            // Fields the file left out arrive as null despite the non-nullable types
            .Select(i => i with { Body = i.Body ?? "", Kind = i.Kind ?? "info" })
            .ToList();
        return feed with { Items = usable.AsReadOnly() };
    }

    public string ToJson() => JsonSerializer.Serialize(this, s_json);
}

/// <summary>Decides which messages to show now. Pure: everything comes in as a parameter.</summary>
public static class NewsSelector
{
    /// <summary>At most this many messages at once, so a long absence does not flood the user.</summary>
    public const int MaxShownAtOnce = 3;

    /// <summary>
    /// The messages that are due, not yet seen and meant for this app version, newest first.
    /// </summary>
    public static IReadOnlyList<NewsItem> Pending(
        NewsFeed feed,
        IReadOnlySet<string> seenIds,
        DateTimeOffset now,
        string appVersion)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(seenIds);

        return feed.Items
            .Where(i => !seenIds.Contains(i.Id))
            .Where(i => i.PublishedAt <= now && (i.ExpiresAt is null || i.ExpiresAt > now))
            .Where(i => FitsVersion(i, appVersion))
            .OrderByDescending(i => i.PublishedAt)
            .Take(MaxShownAtOnce)
            .ToList()
            .AsReadOnly();
    }

    private static bool FitsVersion(NewsItem item, string appVersion)
    {
        if (!UpdatePolicy.TryParse(appVersion, out var app)) return true;

        if (UpdatePolicy.TryParse(item.MinVersion, out var min) && app < min) return false;
        if (UpdatePolicy.TryParse(item.MaxVersion, out var max) && app > max) return false;
        return true;
    }
}
