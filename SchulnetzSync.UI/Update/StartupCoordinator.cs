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

/// <summary>
/// Drives the start: config, update check, initialisation — and keeps the
/// loading view on screen long enough that it does not merely flash.
///
/// Kennt nur <see cref="IUpdateSource"/> und die Typen aus Core, keine
/// WinRT-Klassen. Die Ansicht bekommt Fortschritt und Status über Rückrufe.
/// </summary>
public sealed class StartupCoordinator(
    IUpdateSource     source,
    IProgress<double> progress,
    IProgress<string> status,
    bool              bypassSnooze = false)
{
    /// <summary>Hard cap for the network check.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMilliseconds(2500);

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
    /// Runs the startup steps. Wirft nicht: Jeder Fehler der Prüfung endet in
    /// <see cref="StartupOutcome.Continue"/>.
    /// </summary>
    /// <param name="skipCheck">True when started with /restarted.</param>
    public async Task<StartupOutcome> RunAsync(bool skipCheck, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;

        // Schritt 1 — Konfiguration. Sie liegt beim Aufruf bereits geladen vor;
        // der Schritt macht den echten Fortschritt sichtbar.
        _status.Report("Einstellungen werden geladen…");
        _progress.Report(0.20);

        // Schritt 2 — Update-Prüfung
        _status.Report(skipCheck ? "Wird gestartet…" : "Suche nach Updates…");
        _progress.Report(0.45);

        var gate = new UpdateGate(_source, CheckTimeout);

        // Mit der Attrappe zählt die gespeicherte Frist nicht, sonst liesse
        // sich die Ansicht nach einem «Später erinnern» tagelang nicht mehr
        // aufrufen.
        var prefs = _bypassSnooze ? new UpdatePreferences() : UpdatePreferencesStore.Load();
        var decision = await gate
            .EvaluateAsync(prefs, DateTimeOffset.UtcNow, skipCheck, ct)
            .ConfigureAwait(false);

        OfferedVersion = decision.Version;
        FailureReason  = decision.FailureReason;

        _progress.Report(0.80);

        // Schritt 3 — Initialisierung
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
    /// Installs the update. Windows beendet die App dabei; kehrt der Aufruf
    /// zurück, ist nichts installiert worden.
    /// </summary>
    public async Task InstallAsync(CancellationToken ct)
    {
        _status.Report("Update wird installiert…");
        await _source.InstallAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Records that the user postponed this version.</summary>
    public void Postpone(string version)
    {
        if (_bypassSnooze) return;   // Attrappe hinterlässt keine Spuren

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
