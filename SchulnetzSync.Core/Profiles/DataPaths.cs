using System.Text.Json;

namespace SchulnetzSync.Core.Profiles;

/// <summary>
/// Where things live on disk.
///
///   %LOCALAPPDATA%\Semestria\                 machine-wide: profiles.json, update-prefs.json, crash.log
///   %LOCALAPPDATA%\Semestria\profiles\&lt;id&gt;\  one folder per person: config, cache, tasks, grades, token cache …
///
/// Every per-person file asks <see cref="ProfileFile"/> for its path, so switching the
/// active profile is a matter of pointing <see cref="ProfileDirectory"/> elsewhere and
/// restarting the app.
/// </summary>
public static class DataPaths
{
    /// <summary>The app folder that holds everything.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Semestria");

    /// <summary>Files and folders in <see cref="Root"/> that belong to no single person.</summary>
    public static readonly IReadOnlySet<string> GlobalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "profiles.json", "update-prefs.json", "crash.log", "profiles"
    };

    private static string? _profileDirectory;

    /// <summary>
    /// The active profile's folder. Until <see cref="UseProfile"/> ran this is the root,
    /// which is where everything lived before profiles existed.
    /// </summary>
    public static string ProfileDirectory => _profileDirectory ?? Root;

    /// <summary>Path of a per-person file in the active profile.</summary>
    public static string ProfileFile(string name) => Path.Combine(ProfileDirectory, name);

    /// <summary>Path of a machine-wide file.</summary>
    public static string GlobalFile(string name) => Path.Combine(Root, name);

    public static string DirectoryFor(string root, Guid profileId)
        => Path.Combine(root, "profiles", profileId.ToString("N"));

    /// <summary>Points every per-person path at this profile. Call before anything loads.</summary>
    public static void UseProfile(Guid profileId)
    {
        _profileDirectory = DirectoryFor(Root, profileId);
        Directory.CreateDirectory(_profileDirectory);
    }
}

/// <summary>Reads and writes profiles.json.</summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public static ProfileRegistry? Load(string root)
    {
        var path = Path.Combine(root, "profiles.json");
        if (!File.Exists(path)) return null;

        var registry = JsonSerializer.Deserialize<ProfileRegistry>(File.ReadAllText(path), s_json);
        // A registry without profiles or with a dangling active id is unusable
        return registry is { Profiles.Count: > 0 } && registry.Profiles.Any(p => p.Id == registry.ActiveId)
            ? registry
            : null;
    }

    /// <summary>Written via a temporary file, so a crash never leaves half a list behind.</summary>
    public static void Save(string root, ProfileRegistry registry)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "profiles.json");
        var tmp  = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(registry, s_json));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// Moves the data of the time before profiles into the first profile's folder. Safe to
/// run on every start: it only acts while such data is still lying in the root, and it
/// never overwrites a file the profile already has.
/// </summary>
public static class ProfileMigration
{
    /// <summary>The per-person entries still lying in the root.</summary>
    public static IReadOnlyList<string> LegacyEntries(string root)
        => !Directory.Exists(root)
            ? []
            : Directory.EnumerateFileSystemEntries(root)
                .Where(p => !DataPaths.GlobalNames.Contains(Path.GetFileName(p)))
                .Where(p => !Path.GetFileName(p).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .ToList();

    /// <returns>Number of files and folders moved.</returns>
    public static int MoveIntoProfile(string root, Guid profileId)
    {
        var target = DataPaths.DirectoryFor(root, profileId);
        Directory.CreateDirectory(target);

        int moved = 0;
        foreach (var source in LegacyEntries(root))
        {
            var dest = Path.Combine(target, Path.GetFileName(source));
            if (File.Exists(dest) || Directory.Exists(dest)) continue;   // never overwrite

            if (Directory.Exists(source)) Directory.Move(source, dest);
            else                          File.Move(source, dest);
            moved++;
        }
        return moved;
    }
}

/// <summary>Start-up step shared by the app and the command line.</summary>
public static class ProfileBootstrap
{
    /// <summary>
    /// Loads profiles.json, or creates the first profile on the first start of a version
    /// with profiles, moves any data from before profiles into it, and points
    /// <see cref="DataPaths"/> at the active profile. Must run before anything reads a file.
    /// </summary>
    /// <param name="defaultName">Name for the first profile, e.g. the Windows user name.</param>
    public static ProfileRegistry Initialize(string defaultName, DateTimeOffset now)
    {
        var root     = DataPaths.Root;
        var registry = ProfileStore.Load(root);
        if (registry is null)
        {
            registry = ProfileRegistry.CreateInitial(defaultName, now);
            ProfileStore.Save(root, registry);
        }

        // Data from before profiles always belongs to the profile that owns the old events.
        // Runs every start, so an interrupted move simply continues.
        var heir = registry.Profiles.FirstOrDefault(p => p.OwnsUntaggedEvents) ?? registry.Profiles[0];
        ProfileMigration.MoveIntoProfile(root, heir.Id);

        DataPaths.UseProfile(registry.ActiveId);
        return registry;
    }
}
