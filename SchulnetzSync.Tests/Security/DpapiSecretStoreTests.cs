using System.Security.Cryptography;
using System.Text;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Security;
using Xunit;

namespace SchulnetzSync.Tests.Security;

/// <summary>Feed URLs already on disk must stay readable after the move behind the interface.</summary>
public class DpapiSecretStoreTests
{
    private const string FakeUrl = "https://example.org/feed.ics?token=test";

    private readonly DpapiSecretStore _store = new();

    [Fact]
    public void Secret_RoundTrips()
    {
        byte[] secret = [1, 2, 3, 250];

        Assert.Equal(secret, _store.Unprotect(_store.Protect(secret)));
    }

    [Fact]
    public void ProtectedText_DoesNotContainTheSecret()
    {
        var text = _store.Protect(Encoding.UTF8.GetBytes(FakeUrl));

        Assert.DoesNotContain("token", text);
        Assert.DoesNotContain(FakeUrl, Encoding.UTF8.GetString(Convert.FromBase64String(text)));
    }

    [Fact]
    public void ValueWrittenByV31_IsStillReadable()
    {
        // This is how ConfigManager protected the URL up to version 3.1.
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(FakeUrl), null, DataProtectionScope.CurrentUser);
        var config    = new SyncConfig { FeedUrlEncrypted = Convert.ToBase64String(encrypted) };

        Assert.Equal(FakeUrl, ConfigManager.GetFeedUrl(config));
    }

    [Fact]
    public void ConfigManager_RoundTripsTheFeedUrl()
    {
        var config = new SyncConfig();

        ConfigManager.SetFeedUrl(config, FakeUrl);

        Assert.NotEqual(FakeUrl, config.FeedUrlEncrypted);
        Assert.Equal(FakeUrl, ConfigManager.GetFeedUrl(config));
    }

    [Fact]
    public void ForeignValue_FailsVisibly()
    {
        var foreign = Convert.ToBase64String(Encoding.UTF8.GetBytes("kein DPAPI-Wert"));

        Assert.ThrowsAny<CryptographicException>(() => _store.Unprotect(foreign));
    }
}
