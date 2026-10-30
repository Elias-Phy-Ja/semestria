using SchulnetzSync.Core.Update;

namespace SchulnetzSync.UI.Update;

/// <summary>
/// Claims an update is waiting, so the loading view can be walked through without a store.
///
/// Needed because StoreContext only answers for real in a build actually installed from the
/// Store: under "dotnet run" and with a self-signed MSIX you always end up on the "nothing
/// to do" path and never see the rest.
/// Switched on with <c>--fake-update</c>, or <c>--fake-update-mandatory</c> for the
/// version without a way out. With <c>-slow</c> behind either one it answers too late for
/// the start, which is how the check in the background gets its turn.
/// </summary>
public sealed class FakeUpdateSource(string version, bool mandatory, TimeSpan answerAfter) : IUpdateSource
{
    public async Task<IReadOnlyList<UpdateInfo>> CheckAsync(CancellationToken ct)
    {
        await Task.Delay(answerAfter, ct).ConfigureAwait(false);
        return [new UpdateInfo(version, mandatory)];
    }

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        // Slow enough on purpose that the progress bar can actually be judged.
        for (int percent = 0; percent <= 100; percent += 4)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(percent / 100.0);
            await Task.Delay(120, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Installs nothing, but reports the ending the Store gives in practice: package
    /// swapped, app has to restart itself. That way the last step of the loading view
    /// can be rehearsed without a store.
    /// </summary>
    public Task<InstallOutcome> InstallAsync(CancellationToken ct)
        => Task.FromResult(InstallOutcome.NeedsRestart);
}
