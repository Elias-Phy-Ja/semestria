using System.Text;
using System.Text.Json;
using SchulnetzSync.Core.Profiles;
using SchulnetzSync.Core.Security;

namespace SchulnetzSync.Core.Configuration;

/// <summary>
/// Reads and writes <see cref="SyncConfig"/> in the active profile's folder.
/// The feed URL goes through DPAPI, so the file alone is useless on another machine.
/// </summary>
public static class ConfigManager
{
    // Asked on every use: the folder belongs to the active profile (see DataPaths).
    private static string ConfigDir  => DataPaths.ProfileDirectory;
    private static string ConfigPath => DataPaths.ProfileFile("config.json");

    private static readonly ISecretStore s_secrets = new DpapiSecretStore();

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
        config.FeedUrlEncrypted = s_secrets.Protect(Encoding.UTF8.GetBytes(plainUrl));
    }

    /// <summary>Decrypts the stored feed URL, or null when there is none yet.</summary>
    public static string? GetFeedUrl(SyncConfig config)
    {
        if (config.FeedUrlEncrypted is null) return null;

        return Encoding.UTF8.GetString(s_secrets.Unprotect(config.FeedUrlEncrypted));
    }
}
