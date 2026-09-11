using SchulnetzSync.Core.Update;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Talks to the Microsoft Store. A thin adapter — every rule about when to ask
/// lives in <see cref="UpdatePolicy"/>, which knows nothing about WinRT.
///
/// Funktioniert nur in einer aus dem Store installierten Version. Beim
/// Sideload oder unter «dotnet run» wirft schon GetDefault() oder die Abfrage
/// liefert nichts; beides fängt <see cref="UpdateGate"/> ab.
/// </summary>
public sealed class StoreUpdateSource(IntPtr windowHandle) : IUpdateSource
{
    private readonly IntPtr _windowHandle = windowHandle;

    /// <summary>
    /// Packages found by the last check. Download und Install brauchen genau
    /// diese Objekte — eine erneute Abfrage würde andere Instanzen liefern.
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

        // In dieser Phase wird nur geladen, nicht ersetzt — die App läuft weiter.
        operation.Progress = (_, status) =>
            progress?.Report(Math.Clamp(status.PackageDownloadProgress, 0.0, 1.0));

        var result = await operation.AsTask(ct).ConfigureAwait(false);

        if (result.OverallState is StorePackageUpdateState.Completed
                               or StorePackageUpdateState.Deploying
                               or StorePackageUpdateState.Downloading)
            return;

        throw new InvalidOperationException($"Download endete mit Status {result.OverallState}.");
    }

    public async Task InstallAsync(CancellationToken ct)
    {
        if (_pending is not { Count: > 0 })
            throw new InvalidOperationException("Vor der Installation muss CheckAsync laufen.");

        var context = GetContext();

        // Die Pakete liegen bereits lokal, dieser Aufruf spielt sie ein.
        // Windows beendet die App dabei — der Aufruf kehrt normalerweise nicht
        // zurück. Tut er es doch, ist nichts installiert worden.
        var result = await context
            .RequestDownloadAndInstallStorePackageUpdatesAsync(_pending)
            .AsTask(ct)
            .ConfigureAwait(false);

        throw new InvalidOperationException(
            $"Installation nicht durchgeführt, Status {result.OverallState}.");
    }

    /// <summary>
    /// Der StoreContext einer Desktop-App muss ein Fenster kennen, sonst
    /// scheitert jeder Aufruf, der eine Oberfläche zeigen könnte.
    /// </summary>
    private StoreContext GetContext()
    {
        if (_context is not null) return _context;

        var context = StoreContext.GetDefault()
            ?? throw new InvalidOperationException("Kein Store-Kontext verfügbar.");

        // Bei einer Desktop-App muss der Kontext sein Fenster kennen, sonst
        // scheitert jeder Aufruf, der eine Oberfläche zeigen könnte.
        WinRT.Interop.InitializeWithWindow.Initialize(context, _windowHandle);

        return _context = context;
    }

    private static string FormatVersion(PackageVersion v)
        => $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
}
