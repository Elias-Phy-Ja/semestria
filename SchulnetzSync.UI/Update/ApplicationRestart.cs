using System.Runtime.InteropServices;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Asks Windows to start the app again after it was closed for an update.
///
/// This has to be registered BEFORE the install is kicked off — afterwards Windows ends
/// the process and there is no opportunity left.
///
/// The restart is not guaranteed everywhere (on Windows Server it reportedly never
/// happens), so nothing may depend on it: if it does not come back, the next manual start
/// has to be completely normal.
/// </summary>
public static class ApplicationRestart
{
    /// <summary>Argument Windows passes back, which is how we know it was a restart.</summary>
    public const string RestartedArgument = "/restarted";

    /// <summary>Do not restart after a crash.</summary>
    private const int RestartNoCrash = 1;

    /// <summary>Do not restart after a hang.</summary>
    private const int RestartNoHang = 2;

    private const int MaxCommandLine = 2048;

    /// <summary>Registers the app for an automatic restart.</summary>
    /// <returns>True when Windows accepted the registration.</returns>
    public static bool Register()
    {
        // Restart after an update only, never after a crash or a hang — a program that
        // just fell over should not come back on its own.
        const int flags = RestartNoCrash | RestartNoHang;

        if (RestartedArgument.Length >= MaxCommandLine) return false;

        try
        {
            // Arguments only here; Windows adds the executable itself.
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
