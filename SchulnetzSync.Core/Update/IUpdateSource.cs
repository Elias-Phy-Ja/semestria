namespace SchulnetzSync.Core.Update;

/// <summary>
/// Where updates come from. The production implementation talks to the Microsoft
/// Store; tests use a fake.
///
/// The three calls belong together: <see cref="CheckAsync"/> has to run before
/// <see cref="DownloadAsync"/> and <see cref="InstallAsync"/>, because the store
/// implementation holds on to the packages it found between the steps.
/// </summary>
public interface IUpdateSource
{
    /// <summary>
    /// Asks the store what is available. An empty list means the app is current.
    /// May throw — the caller wraps this in <see cref="UpdateGate"/>.
    /// </summary>
    Task<IReadOnlyList<UpdateInfo>> CheckAsync(CancellationToken ct);

    /// <summary>
    /// Downloads the packages the last <see cref="CheckAsync"/> found. The app keeps
    /// running meanwhile, nothing is replaced yet.
    /// </summary>
    /// <param name="progress">Reports 0.0 to 1.0.</param>
    Task DownloadAsync(IProgress<double>? progress, CancellationToken ct);

    /// <summary>
    /// Installs the downloaded packages.
    ///
    /// Two endings are normal. Either Windows closes the app to swap the package, in
    /// which case this call never returns at all; or it swaps the package underneath
    /// the running app and reports success, which leaves the old code in memory and
    /// means we have to restart ourselves. Anything else throws.
    /// </summary>
    Task<InstallOutcome> InstallAsync(CancellationToken ct);
}
