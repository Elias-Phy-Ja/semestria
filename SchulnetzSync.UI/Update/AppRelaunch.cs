using System.Diagnostics;
using Windows.ApplicationModel;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Starts Semestria again after an update swapped the package underneath the running app.
///
/// The documented path is a different one: Windows closes the app during the install and
/// <see cref="ApplicationRestart"/> brings it back. In practice the install call returns
/// with status "Completed" while the process keeps running with the old code in memory,
/// and then the app has to restart itself.
/// </summary>
public static class AppRelaunch
{
    /// <summary>Application id from the package manifest.</summary>
    private const string ApplicationId = "App";

    /// <summary>
    /// Tries to start a fresh instance through the package identity rather than the exe
    /// path, because after an update that path still points at the previous version folder.
    /// </summary>
    /// <returns>True when a new instance was started.</returns>
    public static bool TryRelaunch()
    {
        if (TryLaunchPackaged()) return true;
        return TryLaunchExecutable();
    }

    private static bool TryLaunchPackaged()
    {
        try
        {
            var family = Package.Current.Id.FamilyName;
            Process.Start(new ProcessStartInfo("explorer.exe",
                $"shell:AppsFolder\\{family}!{ApplicationId}")
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            // Unpackaged (dotnet run) makes Package.Current throw, so fall back to the exe.
            App.LogLine("Neustart über die Paket-Identität nicht möglich: " + ex.Message);
            return false;
        }
    }

    private static bool TryLaunchExecutable()
    {
        try
        {
            if (Environment.ProcessPath is not { } path) return false;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            App.LogLine("Neustart über den Programmpfad nicht möglich: " + ex.Message);
            return false;
        }
    }
}
