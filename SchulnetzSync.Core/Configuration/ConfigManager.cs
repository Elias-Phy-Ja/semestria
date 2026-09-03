using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SchulnetzSync.Core.Configuration;

/// <summary>
/// Loads and saves <see cref="SyncConfig"/> from the user's AppData folder.
/// The feed URL is protected via DPAPI (Windows Data Protection API).
/// </summary>
public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchulnetzSync");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        PropertyNameCaseInsensitive = true,
    };

    // -----------------------------------------------------------------------
    // Load / Save
    // -----------------------------------------------------------------------

    /// <summary>
    /// Loads the config from disk. Returns a fresh default instance if the
    /// file does not exist yet.
    /// </summary>
    public static SyncConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new SyncConfig();

        var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<SyncConfig>(json, JsonOpts) ?? new SyncConfig();
    }

    /// <summary>Persists the config to disk, creating the directory if needed.</summary>
    public static void Save(SyncConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(ConfigPath, json, Encoding.UTF8);
    }

    // -----------------------------------------------------------------------
    // Feed URL encryption (DPAPI — Windows only)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Encrypts the feed URL with DPAPI and stores the Base-64 result in the config.
    /// The plain-text URL is never written to disk.
    /// </summary>
    public static void SetFeedUrl(SyncConfig config, string plainUrl)
    {
        byte[] plain     = Encoding.UTF8.GetBytes(plainUrl);
        byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        config.FeedUrlEncrypted = Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypts and returns the feed URL.
    /// Returns null if no URL has been stored yet.
    /// </summary>
    public static string? GetFeedUrl(SyncConfig config)
    {
        if (config.FeedUrlEncrypted is null) return null;

        byte[] encrypted = Convert.FromBase64String(config.FeedUrlEncrypted);
        byte[] plain     = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
