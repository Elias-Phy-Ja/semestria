using System.Runtime.InteropServices;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Asks Windows to start the app again after it was shut down for an update.
///
/// Muss registriert sein, BEVOR die Installation angestossen wird — danach
/// beendet Windows den Prozess und es gibt keine Gelegenheit mehr dazu.
///
/// Der Neustart ist nicht überall garantiert (unter Windows Server etwa bleibt
/// er berichtet aus). Deshalb darf nichts davon abhängen: Bleibt er aus, muss
/// die App beim nächsten manuellen Start normal hochkommen.
/// </summary>
public static class ApplicationRestart
{
    /// <summary>Command line passed back to the app by the restart.</summary>
    public const string RestartedArgument = "/restarted";

    /// <summary>Do not restart after a crash.</summary>
    private const int RestartNoCrash = 1;

    /// <summary>Do not restart after a hang.</summary>
    private const int RestartNoHang = 2;

    private const int MaxCommandLine = 2048;

    /// <summary>
    /// Registers the app for an automatic restart.
    /// </summary>
    /// <returns>True when Windows accepted the registration.</returns>
    public static bool Register()
    {
        // Nur nach einem Update neu starten, nicht nach Absturz oder Hänger:
        // ein abgestürztes Programm soll nicht von selbst wiederkommen.
        const int flags = RestartNoCrash | RestartNoHang;

        if (RestartedArgument.Length >= MaxCommandLine) return false;

        try
        {
            // Die Kommandozeile enthält nur die Argumente, ohne Exe-Namen.
            int hr = RegisterApplicationRestart(RestartedArgument, flags);
            return hr == 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(
        [MarshalAs(UnmanagedType.LPWStr)] string? commandLine, int flags);
}
