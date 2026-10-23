using SchulnetzSync.Core.News;

namespace SchulnetzSync.Tests.News;

/// <summary>
/// Reading news.json and picking the messages to show. The file is written by hand in the
/// admin page, so the parser has to be forgiving with single bad items but strict about
/// the format version.
/// </summary>
public class NewsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));

    private static NewsItem Item(
        string id,
        DateTimeOffset? published = null,
        DateTimeOffset? expires   = null,
        string? min  = null,
        string? max  = null,
        string? link = null)
        => new(id, $"Titel {id}", "Text", "info", link, null,
               published ?? Now.AddDays(-1), expires, min, max);

    private static IReadOnlyList<NewsItem> Pending(params NewsItem[] items)
        => NewsSelector.Pending(new NewsFeed(1, items), new HashSet<string>(), Now, "2.3.0");

    // ── Parsing ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsTheAdminPageFormat()
    {
        const string json = """
            {
              "version": 1,
              "items": [
                {
                  "id": "umfrage-herbst",
                  "title": "Kurze Umfrage",
                  "body": "Wie gefällt dir Semestria?",
                  "kind": "survey",
                  "link": "https://forms.example.org/abc",
                  "linkLabel": "Zur Umfrage",
                  "publishedAt": "2026-10-24T08:00:00+02:00",
                  "expiresAt": "2026-11-30T00:00:00+01:00"
                }
              ]
            }
            """;

        var feed = NewsFeed.Parse(json);

        var item = Assert.Single(feed.Items);
        Assert.Equal("umfrage-herbst", item.Id);
        Assert.Equal("survey", item.Kind);
        Assert.True(item.HasSafeLink);
        Assert.Null(item.MinVersion);
    }

    /// <summary>
    /// The fixture is a news.json written by the PHP admin page itself (docs/admin), so
    /// this pins down that the app reads exactly what the admin writes.
    /// </summary>
    [Fact]
    public void Parse_ReadsWhatTheAdminPageWrites()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "news-from-admin.json"));

        var feed = NewsFeed.Parse(json);

        Assert.Equal(2, feed.Items.Count);
        var survey = feed.Items.Single(i => i.Id == "umfrage-herbst");
        Assert.Equal("Zeile 1\nZeile 2", survey.Body);
        Assert.True(survey.HasSafeLink);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(2)), survey.PublishedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.FromHours(1)), survey.ExpiresAt);

        // Fields the admin leaves out when empty arrive as null, not as ""
        var info = feed.Items.Single(i => i.Id != "umfrage-herbst");
        Assert.Null(info.Link);
        Assert.Null(info.ExpiresAt);
    }

    [Fact]
    public void Parse_SkipsItemsWithoutIdOrTitle()
    {
        const string json = """{"version":1,"items":[{"id":"","title":"x","publishedAt":"2026-10-01T00:00:00Z"},{"id":"a","title":"A","publishedAt":"2026-10-01T00:00:00Z"}]}""";

        var item = Assert.Single(NewsFeed.Parse(json).Items);
        Assert.Equal("", item.Body);
        Assert.Equal("info", item.Kind);
    }

    [Fact]
    public void Parse_RejectsAnUnknownFormatVersion()
        => Assert.Throws<FormatException>(() => NewsFeed.Parse("""{"version":2,"items":[]}"""));

    [Fact]
    public void Parse_RoundTripsThroughToJson()
    {
        var feed = new NewsFeed(1, [Item("a", link: "https://example.org")]);

        Assert.Equal(feed.Items, NewsFeed.Parse(feed.ToJson()).Items);
    }

    // ── Links ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://forms.office.com/r/abc", true)]
    [InlineData("http://example.org",             false)]
    [InlineData("file:///C:/Windows/system32",    false)]
    [InlineData("javascript:alert(1)",            false)]
    [InlineData("forms.office.com",               false)]
    [InlineData(null,                             false)]
    public void OnlyHttpsLinksAreSafe(string? link, bool safe)
        => Assert.Equal(safe, Item("a", link: link).HasSafeLink);

    // ── Selection ───────────────────────────────────────────────────────

    [Fact]
    public void SeenItems_AreNotShownAgain()
    {
        var feed = new NewsFeed(1, [Item("a"), Item("b")]);

        var pending = NewsSelector.Pending(feed, new HashSet<string> { "a" }, Now, "2.3.0");

        Assert.Equal("b", Assert.Single(pending).Id);
    }

    [Fact]
    public void FutureAndExpiredItems_AreNotShown()
    {
        var pending = Pending(
            Item("future",  published: Now.AddHours(1)),
            Item("expired", expires:   Now.AddMinutes(-1)),
            Item("current", expires:   Now.AddDays(3)));

        Assert.Equal("current", Assert.Single(pending).Id);
    }

    [Fact]
    public void VersionRange_IsRespected()
    {
        var pending = Pending(
            Item("only-new",  min: "2.4.0"),
            Item("only-old",  max: "2.2.0"),
            Item("for-us",    min: "2.3.0", max: "2.3.0.0"),
            Item("for-all"));

        Assert.Equal(["for-all", "for-us"], pending.Select(i => i.Id).Order().ToList());
    }

    [Fact]
    public void NewestFirst_AndCapped()
    {
        var items = Enumerable.Range(1, 6)
            .Select(d => Item($"n{d}", published: Now.AddDays(-d)))
            .ToArray();

        var pending = Pending(items);

        Assert.Equal(NewsSelector.MaxShownAtOnce, pending.Count);
        Assert.Equal("n1", pending[0].Id);
    }

    // ── Source ──────────────────────────────────────────────────────────

    [Fact]
    public void HttpNewsSource_RefusesPlainHttp()
        => Assert.Throws<ArgumentException>(() => new HttpNewsSource(new HttpClient(), "http://example.org/news.json"));
}
