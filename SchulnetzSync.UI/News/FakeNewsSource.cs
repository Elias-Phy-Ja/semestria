using SchulnetzSync.Core.News;

namespace SchulnetzSync.UI.News;

/// <summary>
/// Sample messages for --fake-news, so the news window can be checked without the server.
/// One of each kind, plus one with a link the app must refuse to open.
/// </summary>
public sealed class FakeNewsSource : INewsSource
{
    public Task<NewsFeed> FetchAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.Now;
        return Task.FromResult(new NewsFeed(1,
        [
            new NewsItem("fake-survey", "Kurze Umfrage zu Semestria",
                "Wie gefällt dir die App? Die Umfrage dauert zwei Minuten und ist anonym.",
                "survey", "https://example.org/umfrage", "Zur Umfrage", now.AddHours(-1), now.AddDays(7), null, null),
            new NewsItem("fake-info", "Neu: Noten-Zielrechner",
                "Unter «Noten» kannst du deine Noten eintragen und siehst, ob die Promotion reicht.",
                "info", null, null, now.AddDays(-1), null, null, null),
            new NewsItem("fake-unsafe", "Link wird nicht angeboten",
                "Dieser Eintrag hat einen http-Link. Die App zeigt dafür keinen Button.",
                "update", "http://example.org", "Sollte fehlen", now.AddDays(-2), null, null, null)
        ]));
    }
}
