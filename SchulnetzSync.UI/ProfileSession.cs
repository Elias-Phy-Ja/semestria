using System.IO;
using SchulnetzSync.Core.Profiles;
using SchulnetzSync.UI.Update;

namespace SchulnetzSync.UI;

/// <summary>
/// The profile the app runs as, and the actions of the switcher.
///
/// Switching restarts the app in the other profile instead of swapping data at runtime.
/// Every page, the tray, the timers and AppState then start cleanly from the new folder,
/// and nothing of the previous person can linger in memory — like two separate apps.
/// </summary>
public static class ProfileSession
{
    private static ProfileRegistry? _registry;

    public static ProfileRegistry Registry
        => _registry ?? throw new InvalidOperationException("ProfileSession.Initialize has not run.");

    public static Profile Active => Registry.Active;

    /// <summary>Which Outlook events this profile may touch.</summary>
    public static CalendarOwner Owner => Active.CalendarOwner;

    /// <summary>Must be the very first thing at start-up: every file path depends on it.</summary>
    public static void Initialize()
    {
        if (PackagedDataRoot() is { } packaged) DataPaths.UseRoot(packaged);
        _registry = ProfileBootstrap.Initialize(DefaultName(), DateTimeOffset.Now);
    }

    /// <summary>
    /// The Store build's own data folder, addressed directly: the place where version 2.3
    /// and earlier ended up through Windows' redirection of %LOCALAPPDATA%. Null without a
    /// package identity (dotnet run, the command line), which keeps %LOCALAPPDATA%\Semestria.
    /// </summary>
    private static string? PackagedDataRoot()
    {
        try
        {
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Local", "Semestria");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // No package identity: running unpackaged, which is a normal case, not an error
            return null;
        }
    }

    /// <summary>The Windows user name with a capital letter, e.g. "elias" → "Elias".</summary>
    private static string DefaultName()
    {
        var name = Environment.UserName;
        return name.Length == 0 ? "Profil" : char.ToUpper(name[0]) + name[1..];
    }

    /// <summary>Restarts the app in another profile.</summary>
    public static void SwitchTo(Guid id)
    {
        if (id == Registry.ActiveId) return;
        Save(Registry.Activate(id));
        Restart();
    }

    /// <summary>Creates a profile and restarts into it; the onboarding then sets it up.</summary>
    public static void AddAndSwitch(string name)
    {
        Save(Registry.Add(name, DateTimeOffset.Now, out _));
        Restart();
    }

    public static void Rename(Guid id, string name)
    {
        Save(Registry.Rename(id, name));
        AppState.Notify();
    }

    /// <summary>
    /// Deletes a profile with all of its data on this computer. Removing the active one
    /// restarts the app in the profile that takes over.
    /// </summary>
    public static void Remove(Guid id)
    {
        bool wasActive = id == Registry.ActiveId;
        Save(Registry.Remove(id));

        // Its files are still open while it is the running profile, so delete after the
        // switch: the next start cleans up folders of profiles that no longer exist.
        if (wasActive) { Restart(); return; }
        DeleteFolder(id);
        AppState.Notify();
    }

    /// <summary>
    /// The onboarding of a freshly added profile was cancelled: drop that profile and go
    /// back to the one before. False when there is nothing to go back to.
    /// </summary>
    public static bool AbandonUnfinishedProfile()
    {
        if (Registry.Profiles.Count < 2 || Registry.PreviousId is null) return false;
        Save(Registry.Remove(Registry.ActiveId));
        Restart();
        return true;
    }

    /// <summary>Removes folders whose profile is gone. Runs at start, before anything is open.</summary>
    public static void CleanUpRemovedProfiles()
    {
        var dir = Path.Combine(DataPaths.Root, "profiles");
        if (!Directory.Exists(dir)) return;

        var known = Registry.Profiles.Select(p => p.Id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.EnumerateDirectories(dir))
        {
            if (known.Contains(Path.GetFileName(folder))) continue;
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) { App.LogLine($"Profilordner nicht gelöscht: {ex.Message}"); }
        }
    }

    private static void DeleteFolder(Guid id)
    {
        var folder = DataPaths.DirectoryFor(DataPaths.Root, id);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static void Save(ProfileRegistry registry)
    {
        ProfileStore.Save(DataPaths.Root, registry);
        _registry = registry;
    }

    private static void Restart()
    {
        if (!AppRelaunch.TryRelaunch())
        {
            MessageBox.Show("Semestria konnte sich nicht neu starten. Bitte öffne die App noch einmal, " +
                            "sie startet dann im gewählten Profil.",
                            "Profil gewechselt", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        Application.Current.Shutdown();
    }
}
