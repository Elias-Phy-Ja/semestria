using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

    /// <summary>
    /// Restart after an update. Starting the new instance right away is too early: Windows
    /// only swaps the package once every process of it has ended, and a fresh instance keeps
    /// the old version in use. So a small helper outside the package waits until this process
    /// is gone and the installed version has changed (at most 90 seconds), then starts the app.
    /// </summary>
    /// <returns>True when the helper is running; the caller then only has to exit.</returns>
    public static bool TryRelaunchAfterUpdate()
    {
        try
        {
            var id      = Package.Current.Id;
            var version = $"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}";
            var script  = $$"""
                try { Wait-Process -Id {{Environment.ProcessId}} -Timeout 60 -ErrorAction Stop } catch { }
                $deadline = (Get-Date).AddSeconds(90)
                while ((Get-Date) -lt $deadline) {
                    $installed = @(Get-AppxPackage -Name '{{id.Name}}' | ForEach-Object { $_.Version })
                    if ($installed.Count -gt 0 -and $installed -notcontains '{{version}}') { break }
                    Start-Sleep -Milliseconds 500
                }
                Start-Sleep -Seconds 1
                Start-Process 'shell:AppsFolder\{{id.FamilyName}}!{{ApplicationId}}'
                """;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            // cmd itself still belongs to the package but ends at once; "start" hands the
            // helper over to a process that runs outside of it thanks to the breakaway flag.
            StartOutsidePackage(
                $"cmd.exe /d /c start \"\" /b powershell.exe -NoProfile -NonInteractive " +
                $"-ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}");
            return true;
        }
        catch (Exception ex)
        {
            App.LogLine("Neustart-Helfer nach dem Update nicht gestartet: " + ex.Message);
            return false;
        }
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

    // ── Process creation with desktop-app breakaway ─────────────────────

    /// <summary>Children of the new process start outside the package.</summary>
    private const nint BreakawayEnableProcessTree = 0x01;

    private const nint ProcThreadAttributeDesktopAppPolicy = 0x00020012;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow             = 0x08000000;

    /// <summary>
    /// Starts a process whose own children run outside the package, so they keep running
    /// while Windows replaces it and do not hold the old version in use.
    /// </summary>
    private static void StartOutsidePackage(string commandLine)
    {
        nint size = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref size);
        var attributes = Marshal.AllocHGlobal(size);
        var policy     = Marshal.AllocHGlobal(sizeof(long));
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                Marshal.WriteInt64(policy, BreakawayEnableProcessTree);
                if (!UpdateProcThreadAttribute(attributes, 0, ProcThreadAttributeDesktopAppPolicy,
                                               policy, sizeof(uint), 0, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var startup = new StartupInfoEx { AttributeList = attributes };
                startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();

                if (!CreateProcess(null, new StringBuilder(commandLine), 0, 0, false,
                                   ExtendedStartupInfoPresent | CreateNoWindow, 0, null,
                                   ref startup, out var info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                CloseHandle(info.Process);
                CloseHandle(info.Thread);
            }
            finally
            {
                DeleteProcThreadAttributeList(attributes);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(policy);
            Marshal.FreeHGlobal(attributes);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public nint Reserved2Ptr, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute,
        nint value, nint size, nint previousValue, nint returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint list);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string? application, StringBuilder commandLine,
        nint processAttributes, nint threadAttributes, bool inheritHandles, uint creationFlags,
        nint environment, string? currentDirectory, ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
