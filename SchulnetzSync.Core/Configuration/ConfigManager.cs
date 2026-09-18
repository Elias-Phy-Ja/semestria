using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SchulnetzSync.Core.Configuration;

/// <summary>
/// Reads and writes <see cref="SyncConfig"/> in the user AppData folder.
/// The feed URL goes through DPAPI, so the file alone is useless on another machine.
/// </summary>
public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        PropertyNameCaseInsensitive = true,
    };

    // ── Load / Save ─────────────────────────────────────────────────────────

    /// <summary>Loads the config, or a fresh default one on first start.</summary>
    public static SyncConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new SyncConfig();

        var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<SyncConfig>(json, JsonOpts) ?? new SyncConfig();
    }

    /// <summary>Writes the config, creating the folder on the way if needed.</summary>
    public static void Save(SyncConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(ConfigPath, json, Encoding.UTF8);
    }

    // ── Feed URL, DPAPI (Windows only) ──────────────────────────────────────

    /// <summary>
    /// Encrypts the feed URL and stores the Base-64 result. The plain URL is never
    /// written anywhere, because it contains the personal token.
    /// </summary>
    public static void SetFeedUrl(SyncConfig config, string plainUrl)
    {
        byte[] plain     = Encoding.UTF8.GetBytes(plainUrl);
        byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        config.FeedUrlEncrypted = Convert.ToBase64String(encrypted);
    }

    /// <summary>Decrypts the stored feed URL, or null when there is none yet.</summary>
    public static string? GetFeedUrl(SyncConfig config)
    {
        if (config.FeedUrlEncrypted is null) return null;

        byte[] encrypted = Convert.FromBase64String(config.FeedUrlEncrypted);
        byte[] plain     = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
