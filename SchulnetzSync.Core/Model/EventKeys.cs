namespace SchulnetzSync.Core.Model;

/// <summary>
/// Conventions for the correlation keys used throughout the app.
///
/// Feed events carry the inner Schulnetz id ("P_65100", "T_7409"). Events the
/// user created by hand in the app get a generated key with a fixed prefix, so
/// the sync engine can tell them apart: they come from the local store rather
/// than the feed and are therefore not subject to the feed safety rules.
/// </summary>
public static class EventKeys
{
    /// <summary>Prefix of keys belonging to hand-made events.</summary>
    public const string ManualPrefix = "MANUAL_";

    /// <summary>True when the key belongs to an event the user created in the app.</summary>
    public static bool IsManual(string? key)
        => key is not null && key.StartsWith(ManualPrefix, StringComparison.Ordinal);

    /// <summary>Builds the stable key for a hand-made event.</summary>
    public static string ForManual(Guid id) => ManualPrefix + id.ToString("N");
}
