using System.IO;
using System.Text.Json;
using SchulnetzSync.Core.Update;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Loads and saves the snooze state.
///
/// Sits under %LOCALAPPDATA%\Semestria next to the config, the tasks and the cache —
/// deliberately not in ApplicationData.Current.LocalFolder, which throws for unpackaged
/// starts and therefore on every "dotnet run" during development.
/// </summary>
public static class UpdatePreferencesStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "update-prefs.json");

    /// <summary>
    /// Reads the state. A missing or damaged file falls back to the default,
    /// because nothing here is worth holding up the start for.
    /// </summary>
    public static UpdatePreferences Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(Path))
                       ?? new UpdatePreferences();
        }
        catch { /* fall back to the default */ }

        return new UpdatePreferences();
    }

    public static void Save(UpdatePreferences prefs)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(prefs));
        }
        catch { /* postponing is a convenience, not something worth failing over */ }
    }
}
