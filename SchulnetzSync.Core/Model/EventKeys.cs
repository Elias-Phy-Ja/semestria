namespace SchulnetzSync.Core.Model;

/// <summary>
/// Correlation keys used across the app. Feed events carry the inner Schulnetz id
/// ("P_65100", "T_7409"); events the user typed in by hand get a prefixed key instead
/// so the sync engine can tell them apart. They never came from the feed, so the feed
/// safety rules must not apply to them.
/// </summary>
public static class EventKeys
{
    /// <summary>Prefix that marks a hand-made event.</summary>
    public const string ManualPrefix = "MANUAL_";

    /// <summary>True when the key belongs to an event the user created in the app.</summary>
    public static bool IsManual(string? key)
        => key is not null && key.StartsWith(ManualPrefix, StringComparison.Ordinal);

    /// <summary>Builds the stable key for a hand-made event.</summary>
    public static string ForManual(Guid id) => ManualPrefix + id.ToString("N");
}
