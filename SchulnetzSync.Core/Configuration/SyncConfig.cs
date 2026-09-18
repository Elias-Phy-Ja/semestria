using System.Text.Json.Serialization;
using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Configuration;

/// <summary>
/// Everything the app remembers between runs. Lands as JSON in
/// %LOCALAPPDATA%\Semestria\config.json, with the feed URL encrypted via DPAPI.
/// </summary>
public sealed class SyncConfig
{
    /// <summary>
    /// Feed URL as a DPAPI-encrypted Base-64 string. The plain value never goes
    /// to disk and never into a log.
    /// </summary>
    public string? FeedUrlEncrypted { get; set; }

    /// <summary>Entra (Azure AD) client id of the registered app.</summary>
    public string? ClientId { get; set; }

    /// <summary>Target calendar. Null means the primary calendar.</summary>
    public string? CalendarId { get; set; }

    /// <summary>
    /// Types that go to Outlook. Exams only by default — Termine are opt-in, because
    /// the feed carries hundreds of them and would flood the calendar unasked.
    /// </summary>
    public HashSet<SchulnetzEventType> EnabledTypes { get; set; } =
        [SchulnetzEventType.Pruefung];

    /// <summary>Retitle vanished exams instead of deleting them.</summary>
    public bool CancelInsteadOfDelete { get; set; } = true;

    /// <summary>Take a missing exam room from the lesson at the same time.</summary>
    public bool EnrichExamLocationFromLesson { get; set; } = true;

    /// <summary>UTC time of the last successful sync run.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Result of the last run in plain words, e.g. "3 neu, 1 aktualisiert".</summary>
    public string? LastRunResult { get; set; }

    /// <summary>
    /// UTC time of the last successful feed download, whether from auto-refresh, the
    /// "Feed laden" button or a full sync. Separate from <see cref="LastRunAt"/>,
    /// which only counts calendar writes.
    /// </summary>
    public DateTimeOffset? LastFeedRefreshAt { get; set; }

    /// <summary>True once the user finished the onboarding wizard.</summary>
    public bool IsOnboardingComplete { get; set; }

    /// <summary>Version of the legal texts the user accepted. 0 means never.</summary>
    public int AcceptedLegalVersion { get; set; }

    /// <summary>Theme used as long as the user has not picked one.</summary>
    public const string DefaultTheme = "Dark";

    /// <summary>"Light", "Dark" or "System". Null means never chosen.</summary>
    public string? ThemePreference { get; set; }

    /// <summary>The theme to apply: the stored choice, or <see cref="DefaultTheme"/>.</summary>
    public string ResolveTheme() => ThemePreference ?? DefaultTheme;

    /// <summary>
    /// Pull the feed quietly on every start. Falls back to the cached events when
    /// offline or on error, so this is safe to leave on.
    /// </summary>
    public bool AutoRefreshFeed { get; set; } = true;

    /// <summary>Hands the diff engine the parts of the config it cares about.</summary>
    public SyncOptions ToSyncOptions() => new()
    {
        EnabledTypes                = EnabledTypes,
        CalendarId                  = CalendarId,
        CancelInsteadOfDelete       = CancelInsteadOfDelete,
        EnrichExamLocationFromLesson = EnrichExamLocationFromLesson,
    };
}
