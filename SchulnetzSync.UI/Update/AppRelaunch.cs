using System.Diagnostics;
using Windows.ApplicationModel;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Starts Semestria again after an update that replaced the package while the
/// app was running.
///
/// Der dokumentierte Weg ist ein anderer: Windows beendet die App während der
/// Installation, und <see cref="ApplicationRestart"/> bringt sie zurück. In der
/// Praxis kommt der Aufruf aber mit Status «Completed» zurück und der Prozess
/// läuft weiter, mit dem alten Code im Speicher. Dann muss die App sich selbst
/// neu starten.
/// </summary>
public static class AppRelaunch
{
    /// <summary>Application-Id aus dem Paketmanifest.</summary>
    private const string ApplicationId = "App";

    /// <summary>
    /// Tries to launch a fresh instance. Über die Paket-Identität, nicht über
    /// den Exe-Pfad: Nach dem Update zeigt der alte Pfad auf den Ordner der
    /// vorherigen Version.
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
            // Unverpackt (dotnet run) wirft Package.Current; dann bleibt die Exe.
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
