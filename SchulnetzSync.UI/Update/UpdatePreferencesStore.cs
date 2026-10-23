using System.IO;
using System.Text.Json;
using SchulnetzSync.Core.Update;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Loads and saves the snooze state.
///
/// Sits in %LOCALAPPDATA%\Semestria next to profiles.json, shared by all profiles —
/// deliberately not in ApplicationData.Current.LocalFolder, which throws for unpackaged
/// starts and therefore on every "dotnet run" during development.
/// </summary>
public static class UpdatePreferencesStore
{
    // Machine-wide: an update is installed once for every profile
    private static readonly string Path = SchulnetzSync.Core.Profiles.DataPaths.GlobalFile("update-prefs.json");

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
