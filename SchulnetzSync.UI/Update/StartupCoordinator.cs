using SchulnetzSync.Core.Update;

namespace SchulnetzSync.UI.Update;

/// <summary>What the loading view should do next.</summary>
public enum StartupOutcome
{
    /// <summary>Hide the loading view and show the app.</summary>
    Continue,

    /// <summary>Show the update prompt with a "Später erinnern" button.</summary>
    PromptOptional,

    /// <summary>Show the update prompt without a way out.</summary>
    PromptMandatory,
}

/// <summary>What a search started from the settings came to.</summary>
public enum ManualUpdateResult
{
    /// <summary>An update was found and the prompt is on screen.</summary>
    PromptShown,

    /// <summary>The store answered and has nothing newer.</summary>
    UpToDate,

    /// <summary>The store gave no usable answer.</summary>
    Failed,
}

/// <summary>
/// Drives the start: config, update check, initialisation, and keeps the loading view on
/// screen long enough that it does not just flash past.
///
/// Knows only <see cref="IUpdateSource"/> and the types from Core, never a WinRT class.
/// The view gets progress and status through callbacks, which is what makes this testable.
/// </summary>
public sealed class StartupCoordinator(
    IUpdateSource     source,
    IProgress<double> progress,
    IProgress<string> status,
    bool              bypassSnooze = false)
{
    /// <summary>Hard cap for the network check.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMilliseconds(2500);

    /// <summary>Limit for a search by hand: nothing is waiting on it, so the store gets time.</summary>
    private static readonly TimeSpan ManualCheckTimeout = TimeSpan.FromSeconds(30);

    /// <summary>So the loading view does not flash by on a fast machine.</summary>
    private static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(800);

    private readonly IUpdateSource     _source       = source;
    private readonly IProgress<double> _progress     = progress;
    private readonly IProgress<string> _status       = status;
    private readonly bool              _bypassSnooze = bypassSnooze;

    /// <summary>Version offered by the store, set once the check found one.</summary>
    public string? OfferedVersion { get; private set; }

    /// <summary>Why the check produced nothing, for the log.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Runs the startup steps. Never throws: every failure of the check ends up as
    /// <see cref="StartupOutcome.Continue"/>, because nothing here is worth a dead start.
    /// </summary>
    /// <param name="skipCheck">True when started with /restarted.</param>
    public async Task<StartupOutcome> RunAsync(bool skipCheck, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;

        // Step 1: config. It is already loaded by the time we get here; this step only
        // gives the progress bar something honest to show.
        _status.Report("Einstellungen werden geladen…");
        _progress.Report(0.20);

        // Step 2: look for updates.
        _status.Report(skipCheck ? "Wird gestartet…" : "Suche nach Updates…");
        _progress.Report(0.45);

        var gate = new UpdateGate(_source, CheckTimeout);

        // With the fake source the stored postponement does not count, otherwise a single
        // "Später erinnern" would make the view unreachable for a day.
        var prefs = _bypassSnooze ? new UpdatePreferences() : UpdatePreferencesStore.Load();
        var decision = await gate
            .EvaluateAsync(prefs, DateTimeOffset.UtcNow, skipCheck, ct)
            .ConfigureAwait(false);

        OfferedVersion = decision.Version;
        FailureReason  = decision.FailureReason;

        _progress.Report(0.80);

        // Step 3: the rest of the startup work.
        _status.Report("Wird vorbereitet…");
        await EnsureMinimumVisibleAsync(started, ct).ConfigureAwait(false);
        _progress.Report(1.0);

        return decision.Prompt switch
        {
            UpdatePrompt.Mandatory => StartupOutcome.PromptMandatory,
            UpdatePrompt.Optional  => StartupOutcome.PromptOptional,
            _                      => StartupOutcome.Continue,
        };
    }

    /// <summary>
    /// Searches on request while the app is running, postponed updates included. Download
    /// and install work afterwards exactly as after the check at the start.
    /// </summary>
    public async Task<StartupOutcome> CheckNowAsync(CancellationToken ct)
    {
        var gate     = new UpdateGate(_source, ManualCheckTimeout);
        var decision = await gate.EvaluateIgnoringSnoozeAsync(DateTimeOffset.UtcNow, ct).ConfigureAwait(false);

        OfferedVersion = decision.Version;
        FailureReason  = decision.FailureReason;

        return decision.Prompt switch
        {
            UpdatePrompt.Mandatory => StartupOutcome.PromptMandatory,
            UpdatePrompt.Optional  => StartupOutcome.PromptOptional,
            _                      => StartupOutcome.Continue,
        };
    }

    /// <summary>Downloads the update, reporting progress to the loading view.</summary>
    public async Task DownloadAsync(CancellationToken ct)
    {
        _status.Report("Update wird geladen…");
        _progress.Report(0.0);

        await _source
            .DownloadAsync(new Progress<double>(p => _progress.Report(p)), ct)
            .ConfigureAwait(false);

        _progress.Report(1.0);
    }

    /// <summary>
    /// Installs the update. If Windows closes the app to do it, this never returns;
    /// otherwise the result says what is still missing.
    /// </summary>
    public async Task<InstallOutcome> InstallAsync(CancellationToken ct)
    {
        _status.Report("Update wird installiert…");
        return await _source.InstallAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Records that the user postponed this version.</summary>
    public void Postpone(string version)
    {
        if (_bypassSnooze) return;   // the fake source leaves no traces on disk

        var prefs = UpdatePreferencesStore.Load();
        UpdatePreferencesStore.Save(UpdatePolicy.Postpone(prefs, version, DateTimeOffset.UtcNow));
    }

    private static async Task EnsureMinimumVisibleAsync(DateTimeOffset started, CancellationToken ct)
    {
        var elapsed = DateTimeOffset.UtcNow - started;
        if (elapsed < MinimumVisible)
            await Task.Delay(MinimumVisible - elapsed, ct).ConfigureAwait(false);
    }
}
