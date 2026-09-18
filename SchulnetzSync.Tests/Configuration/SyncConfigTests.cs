using System.Text.Json;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Model;
using Xunit;

namespace SchulnetzSync.Tests.Configuration;

/// <summary>Defaults that must not drift, plus what happens to a saved config on update.</summary>
public class SyncConfigTests
{
    [Fact]
    public void NewConfig_SyncsOnlyPruefungen()
    {
        var config = new SyncConfig();

        Assert.Equal([SchulnetzEventType.Pruefung], config.EnabledTypes);
    }

    [Fact]
    public void NewConfig_UsesDarkTheme()
    {
        var config = new SyncConfig();

        Assert.Null(config.ThemePreference);
        Assert.Equal("Dark", config.ResolveTheme());
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("System")]
    public void ChosenTheme_WinsOverDefault(string choice)
    {
        var config = new SyncConfig { ThemePreference = choice };

        Assert.Equal(choice, config.ResolveTheme());
    }

    [Fact]
    public void SavedTypes_AreKeptWhenLoading()
    {
        // Someone who switched Termine on keeps them after an update.
        var saved  = new SyncConfig { EnabledTypes = [SchulnetzEventType.Pruefung, SchulnetzEventType.Termin] };
        var loaded = JsonSerializer.Deserialize<SyncConfig>(JsonSerializer.Serialize(saved))!;

        Assert.Equal(2, loaded.EnabledTypes.Count);
        Assert.Contains(SchulnetzEventType.Termin, loaded.EnabledTypes);
    }
}
