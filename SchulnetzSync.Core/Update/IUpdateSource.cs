namespace SchulnetzSync.Core.Update;

/// <summary>
/// Where updates come from. The production implementation talks to the
/// Microsoft Store; tests use a fake.
///
/// Die drei Aufrufe hängen zusammen: <see cref="CheckAsync"/> muss vor
/// <see cref="DownloadAsync"/> und <see cref="InstallAsync"/> laufen, weil die
/// Store-Implementierung die gefundenen Pakete zwischen den Schritten festhält.
/// </summary>
public interface IUpdateSource
{
    /// <summary>
    /// Asks the store what is available. An empty list means the app is current.
    /// May throw — the caller wraps this in <see cref="UpdateGate"/>.
    /// </summary>
    Task<IReadOnlyList<UpdateInfo>> CheckAsync(CancellationToken ct);

    /// <summary>
    /// Downloads the packages found by the last <see cref="CheckAsync"/>.
    /// The app keeps running during this; nothing is replaced yet.
    /// </summary>
    /// <param name="progress">Reports 0.0 to 1.0.</param>
    Task DownloadAsync(IProgress<double>? progress, CancellationToken ct);

    /// <summary>
    /// Installs the downloaded packages.
    ///
    /// Zwei Ausgänge sind normal: Windows beendet die App, um das Paket zu
    /// ersetzen, dann kehrt der Aufruf gar nicht zurück. Oder es ersetzt das
    /// Paket, während die App läuft, und meldet Erfolg zurück; dann läuft im
    /// Speicher noch der alte Code und die App muss neu gestartet werden.
    /// Alles andere wirft.
    /// </summary>
    Task<InstallOutcome> InstallAsync(CancellationToken ct);
}
