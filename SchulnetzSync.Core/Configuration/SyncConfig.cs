using System.Text.Json.Serialization;
using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Core.Configuration;

/// <summary>
/// Persisted configuration for SchulnetzSync.
/// Stored as JSON in %LOCALAPPDATA%\SchulnetzSync\config.json.
/// The feed URL is stored encrypted (DPAPI on Windows).
/// </summary>
public sealed class SyncConfig
{
    /// <summary>
    /// Feed URL, DPAPI-encrypted Base-64 string.
    /// Never store or log the plain-text value.
    /// </summary>
    public string? FeedUrlEncrypted { get; set; }

    /// <summary>Entra (Azure AD) Client-ID of the registered app.</summary>
    public string? ClientId { get; set; }

    /// <summary>Target calendar ID. Null = primary calendar.</summary>
    public string? CalendarId { get; set; }

    /// <summary>Which event types to synchronise. Default: both.</summary>
    public HashSet<SchulnetzEventType> EnabledTypes { get; set; } =
        [SchulnetzEventType.Pruefung, SchulnetzEventType.Termin];

    /// <summary>Mark exams as cancelled instead of deleting when they vanish.</summary>
    public bool CancelInsteadOfDelete { get; set; } = true;

    /// <summary>Enrich exam room from the concurrent lesson.</summary>
    public bool EnrichExamLocationFromLesson { get; set; } = true;

    /// <summary>UTC timestamp of the last successful sync run.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Human-readable result of the last run (e.g. "3 neu, 1 aktualisiert").</summary>
    public string? LastRunResult { get; set; }

    /// <summary>True after the user has completed the onboarding wizard.</summary>
    public bool IsOnboardingComplete { get; set; }

    /// <summary>Version of the legal documents the user accepted. 0 = never accepted.</summary>
    public int AcceptedLegalVersion { get; set; }

    /// <summary>Theme preference: "Light" | "Dark" | null = System.</summary>
    public string? ThemePreference { get; set; }

    /// <summary>Converts config to a <see cref="SyncOptions"/> for the diff engine.</summary>
    public SyncOptions ToSyncOptions() => new()
    {
        EnabledTypes                = EnabledTypes,
        CalendarId                  = CalendarId,
        CancelInsteadOfDelete       = CancelInsteadOfDelete,
        EnrichExamLocationFromLesson = EnrichExamLocationFromLesson,
    };
}
