using SchulnetzSync.Core.Update;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Pretends an update is available, for walking through the loading view
/// without a store.
///
/// Nötig, weil StoreContext erst in einer aus dem Store installierten Version
/// echte Antworten gibt: Unter «dotnet run» und beim selbstsignierten MSIX
/// läuft sonst immer nur der Pfad «kein Update».
/// Aktiviert mit <c>--fake-update</c>, zwingend mit <c>--fake-update-mandatory</c>.
/// </summary>
public sealed class FakeUpdateSource(string version, bool mandatory) : IUpdateSource
{
    public Task<IReadOnlyList<UpdateInfo>> CheckAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<UpdateInfo>>([new UpdateInfo(version, mandatory)]);

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        // Ein Download, der lange genug dauert, um den Balken zu beurteilen.
        for (int percent = 0; percent <= 100; percent += 4)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(percent / 100.0);
            await Task.Delay(120, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Installiert nichts, meldet aber den Ausgang, den der Store in der Praxis
    /// liefert: Paket ersetzt, App muss sich selbst neu starten. So lässt sich
    /// der Abschluss der Ladeansicht ohne Store durchspielen.
    /// </summary>
    public Task<InstallOutcome> InstallAsync(CancellationToken ct)
        => Task.FromResult(InstallOutcome.NeedsRestart);
}
