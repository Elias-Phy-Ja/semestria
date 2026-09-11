using System.IO;
using System.Text.Json;
using SchulnetzSync.Core.Update;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Loads and saves the snooze state.
///
/// Liegt unter %LOCALAPPDATA%\Semestria wie Config, Aufgaben und Cache — nicht
/// in ApplicationData.Current.LocalFolder, denn das wirft bei unverpackten
/// Starts und damit bei jedem «dotnet run» während der Entwicklung.
/// </summary>
public static class UpdatePreferencesStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Semestria", "update-prefs.json");

    /// <summary>
    /// Reads the state. Fehlt die Datei oder ist sie beschädigt, gilt der
    /// Standardzustand — eine kaputte Datei darf den Start nicht aufhalten.
    /// </summary>
    public static UpdatePreferences Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(Path))
                       ?? new UpdatePreferences();
        }
        catch { /* Standardzustand */ }

        return new UpdatePreferences();
    }

    public static void Save(UpdatePreferences prefs)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(prefs));
        }
        catch { /* Verschieben ist eine Bequemlichkeit, kein Muss */ }
    }
}
