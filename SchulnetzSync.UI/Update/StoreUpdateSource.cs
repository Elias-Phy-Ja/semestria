using SchulnetzSync.Core.Update;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Talks to the Microsoft Store. A thin adapter on purpose — every rule about when to ask
/// lives in <see cref="UpdatePolicy"/>, which knows nothing about WinRT.
///
/// Only works in a build installed from the Store. Sideloaded or under "dotnet run" either
/// GetDefault() throws outright or the query comes back empty; <see cref="UpdateGate"/>
/// catches both and lets the app start regardless.
/// </summary>
public sealed class StoreUpdateSource(IntPtr windowHandle) : IUpdateSource
{
    private readonly IntPtr _windowHandle = windowHandle;

    /// <summary>
    /// What the last check found. Download and install need exactly these objects;
    /// asking again would hand back different instances and they would not match.
    /// </summary>
    private IReadOnlyList<StorePackageUpdate>? _pending;

    private StoreContext? _context;

    public async Task<IReadOnlyList<UpdateInfo>> CheckAsync(CancellationToken ct)
    {
        var context = GetContext();

        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(ct)
                                   .ConfigureAwait(false);

        _pending = updates;

        return updates
            .Select(u => new UpdateInfo(
                Version:     FormatVersion(u.Package.Id.Version),
                IsMandatory: u.Mandatory))
            .ToList();
    }

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (_pending is not { Count: > 0 })
            throw new InvalidOperationException("Vor dem Download muss CheckAsync laufen.");

        var context   = GetContext();
        var operation = context.RequestDownloadStorePackageUpdatesAsync(_pending);

        // This stage only downloads, it does not replace anything — the app keeps running.
        operation.Progress = (_, status) =>
            progress?.Report(Math.Clamp(status.PackageDownloadProgress, 0.0, 1.0));

        var result = await operation.AsTask(ct).ConfigureAwait(false);

        if (result.OverallState is StorePackageUpdateState.Completed
                               or StorePackageUpdateState.Deploying
                               or StorePackageUpdateState.Downloading)
            return;

        throw new InvalidOperationException($"Download endete mit Status {result.OverallState}.");
    }

    public async Task<InstallOutcome> InstallAsync(CancellationToken ct)
    {
        if (_pending is not { Count: > 0 })
            throw new InvalidOperationException("Vor der Installation muss CheckAsync laufen.");

        var context = GetContext();

        // The packages are already on disk; this call puts them in place. If Windows
        // closes the app to do it, this call never returns at all.
        var result = await context
            .RequestDownloadAndInstallStorePackageUpdatesAsync(_pending)
            .AsTask(ct)
            .ConfigureAwait(false);

        // Completed means the package was replaced while this process kept running on the
        // old code. Deploying means Windows finishes up in the background. Both of them
        // call for a restart, not for an error message.
        if (result.OverallState is StorePackageUpdateState.Completed
                               or StorePackageUpdateState.Deploying)
            return InstallOutcome.NeedsRestart;

        throw new InvalidOperationException(
            $"Installation nicht durchgeführt, Status {result.OverallState}.");
    }

    /// <summary>Creates the context once and keeps it, because it is tied to the window.</summary>
    private StoreContext GetContext()
    {
        if (_context is not null) return _context;

        var context = StoreContext.GetDefault()
            ?? throw new InvalidOperationException("Kein Store-Kontext verfügbar.");

        // A desktop app has to tell the context which window it belongs to, otherwise
        // every call that might show UI of its own fails.
        WinRT.Interop.InitializeWithWindow.Initialize(context, _windowHandle);

        return _context = context;
    }

    private static string FormatVersion(PackageVersion v)
        => $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
}
