using System.Windows;
using System.Windows.Interop;
using ModernWpf.Controls;
using SchulnetzSync.Core.Update;
using SchulnetzSync.UI.Controls;
using SchulnetzSync.UI.Pages;
using SchulnetzSync.UI.Update;

namespace SchulnetzSync.UI;

/// <summary>
/// The window frame: loading view, the update prompt that sits in front of it, the sidebar
/// navigation, and the handling of a Windows session that is about to end.
///
/// The pages themselves know nothing about any of this. They only get navigated to.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Windows asks whether the session may end.</summary>
    private const int WmQueryEndSession = 0x0011;

    /// <summary>Windows states that the session is ending.</summary>
    private const int WmEndSession = 0x0016;

    private StartupCoordinator? _startup;
    private CancellationTokenSource? _startupCts;

    /// <summary>Version offered by the store while the prompt is up.</summary>
    private string? _offeredVersion;

    /// <summary>True while the prompt must not offer a way into the app.</summary>
    private bool _updateIsMandatory;

    /// <summary>The second try after a check at the start without an answer, while it runs.</summary>
    private Task? _backgroundCheck;

    /// <summary>Runs once the loading view is gone.</summary>
    public event Action? StartupFinished;

    public MainWindow()
    {
        InitializeComponent();
        TxtLoadingVersion.Text = "Version " + AppConstants.Version;
        ShowProfileHeader();
        HookTours();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Loading phase
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Runs the loading view: config, update check, initialisation.</summary>
    /// <param name="restarted">True when Windows restarted us after an update.</param>
    public async Task RunStartupAsync(bool restarted, IUpdateSourceFactory sourceFactory)
    {
        _startupCts = new CancellationTokenSource();

        var handle = new WindowInteropHelper(this).Handle;
        var source = sourceFactory.Create(handle);

        _startup = new StartupCoordinator(
            source,
            new Progress<double>(p => LoadingBar.Value = p),
            new Progress<string>(t => TxtLoadingStatus.Text = t),
            sourceFactory.BypassSnooze);

        StartupOutcome outcome;
        try
        {
            outcome = await _startup.RunAsync(restarted, _startupCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;   // the window is closing
        }

        if (_startup.FailureReason is { } reason)
            App.LogLine($"Update-Prüfung ohne Ergebnis: {reason}");

        if (restarted)
        {
            await ShowRestartedAsync();
            FinishStartup();
            return;
        }

        switch (outcome)
        {
            case StartupOutcome.PromptOptional:
                ShowUpdatePrompt(_startup.OfferedVersion, mandatory: false);
                break;

            case StartupOutcome.PromptMandatory:
                ShowUpdatePrompt(_startup.OfferedVersion, mandatory: true);
                break;

            default:
                FinishStartup();

                // Too slow for the start is not the same as nothing to update: keep asking.
                if (_startup.FailureReason is not null)
                    _backgroundCheck = CheckInBackgroundAsync();
                break;
        }
    }

    /// <summary>
    /// Finishes a check the start gave up on. A mandatory update then locks the app just as
    /// it would have at the start; an optional one only shows a small hint, because by now
    /// the user may be in the middle of typing something.
    /// </summary>
    private async Task CheckInBackgroundAsync()
    {
        StartupOutcome outcome;
        try
        {
            outcome = await _startup!.RecheckAsync(_startupCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;   // the window is closing
        }

        if (_startup.FailureReason is { } reason)
            App.LogLine($"Update-Prüfung im Hintergrund ohne Ergebnis: {reason}");

        switch (outcome)
        {
            case StartupOutcome.PromptMandatory:
                ShowUpdatePrompt(_startup.OfferedVersion, mandatory: true);
                break;

            case StartupOutcome.PromptOptional:
                ShowUpdateHint(_startup.OfferedVersion);
                break;
        }
    }

    private void ShowUpdateHint(string? version)
    {
        _offeredVersion        = version;
        TxtUpdateHintTitle.Text = UpdateHeadline(version);
        UpdateHint.Visibility  = Visibility.Visible;
    }

    /// <summary>A short confirmation after a restart, then on into the app.</summary>
    private async Task ShowRestartedAsync()
    {
        LoadingProgressArea.Visibility = Visibility.Collapsed;
        UpdateDoneArea.Visibility      = Visibility.Visible;
        TxtUpdateDone.Text             = $"Semestria wurde auf Version {AppConstants.Version} aktualisiert.";

        await Task.Delay(1500);
    }

    private void ShowUpdatePrompt(string? version, bool mandatory)
    {
        _offeredVersion    = version;
        _updateIsMandatory = mandatory;

        // The overlay alone only stops the mouse; this also keeps the keyboard out of the app
        NavView.IsEnabled = !mandatory;

        UpdateHint.Visibility          = Visibility.Collapsed;
        LoadingOverlay.Visibility      = Visibility.Visible;
        LoadingProgressArea.Visibility = Visibility.Collapsed;
        UpdateFailureArea.Visibility   = Visibility.Collapsed;
        UpdateDoneArea.Visibility      = Visibility.Collapsed;
        UpdatePromptArea.Visibility    = Visibility.Visible;
        BtnUpdateNow.Visibility        = Visibility.Visible;

        TxtUpdateHeadline.Text = UpdateHeadline(version);

        TxtUpdateBody.Text = mandatory
            ? $"Du hast {AppConstants.Version}. Dieses Update ist erforderlich, "
              + "Semestria kann ohne es nicht fortfahren."
            : $"Du hast {AppConstants.Version}. Das Update kommt aus dem Microsoft Store.";

        // A mandatory update leaves no way into the app.
        BtnUpdateLater.Visibility = mandatory ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The store sometimes reports the version that is already installed. A number that
    /// is not actually higher is therefore left out of the message entirely.
    /// </summary>
    private static string UpdateHeadline(string? version)
        => UpdatePolicy.IsNewerThan(version, AppConstants.Version)
            ? $"Version {version} verfügbar"
            : "Ein Update ist verfügbar";

    /// <summary>
    /// The search behind the button in the settings. When it finds something, the same
    /// prompt as at the start opens in front of the app.
    /// </summary>
    public async Task<ManualUpdateResult> SearchForUpdateAsync()
    {
        if (_startup is null) return ManualUpdateResult.Failed;

        // Never two checks at once: each replaces the packages the install needs later
        if (_backgroundCheck is { } running)
        {
            await running;
            if (LoadingOverlay.Visibility == Visibility.Visible) return ManualUpdateResult.PromptShown;
        }

        StartupOutcome outcome;
        try
        {
            outcome = await _startup.CheckNowAsync(_startupCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return ManualUpdateResult.Failed;   // the window is closing
        }

        if (_startup.FailureReason is { } reason)
        {
            App.LogLine($"Update-Suche ohne Ergebnis: {reason}");
            return ManualUpdateResult.Failed;
        }

        if (outcome == StartupOutcome.Continue) return ManualUpdateResult.UpToDate;

        ShowUpdatePrompt(_startup.OfferedVersion, outcome == StartupOutcome.PromptMandatory);
        return ManualUpdateResult.PromptShown;
    }

    private void FinishStartup()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;

        // The prompt can also come from the settings, long after the start is over
        if (_startupDone) return;

        _startupDone = true;
        StartupFinished?.Invoke();

        // The first page was loaded behind the loading view; its tour may run now
        if (ContentFrame.Content is ITourPage page) MaybeStartTour(page);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Update buttons
    // ═══════════════════════════════════════════════════════════════════════

    private async void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
        => await InstallUpdateAsync();

    private async void BtnUpdateRetry_Click(object sender, RoutedEventArgs e)
        => await InstallUpdateAsync();

    private void BtnUpdateLater_Click(object sender, RoutedEventArgs e)
    {
        if (_offeredVersion is { } version)
            _startup?.Postpone(version);

        FinishStartup();
    }

    private void BtnUpdateHintShow_Click(object sender, RoutedEventArgs e)
        => ShowUpdatePrompt(_offeredVersion, mandatory: false);

    private void BtnUpdateHintLater_Click(object sender, RoutedEventArgs e)
    {
        if (_offeredVersion is { } version)
            _startup?.Postpone(version);

        UpdateHint.Visibility = Visibility.Collapsed;
    }

    private void BtnQuit_Click(object sender, RoutedEventArgs e)
        => System.Windows.Application.Current.Shutdown();

    private async Task InstallUpdateAsync()
    {
        if (_startup is null) return;

        UpdatePromptArea.Visibility    = Visibility.Collapsed;
        UpdateFailureArea.Visibility   = Visibility.Collapsed;
        LoadingProgressArea.Visibility = Visibility.Visible;

        try
        {
            var ct = _startupCts?.Token ?? CancellationToken.None;

            await _startup.DownloadAsync(ct);

            // A running calendar sync must not be cut off mid-write, or Outlook is left
            // with half-finished entries.
            await WaitForSyncToFinishAsync(ct);

            // Has to happen before the install: after it Windows ends the process.
            if (!ApplicationRestart.Register())
                App.LogLine("Automatischer Neustart konnte nicht registriert werden.");

            await _startup.InstallAsync(ct);

            // We only get here when Windows did not close the app. The package has been
            // replaced, but the old code is still the one running.
            await FinishInstalledUpdateAsync();
        }
        catch (OperationCanceledException)
        {
            // The window is closing, so there is nothing left to do.
        }
        catch (Exception ex)
        {
            App.LogLine("Update fehlgeschlagen: " + ex);
            ShowUpdateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Finishes an update that was applied while the app was running: say so briefly,
    /// then restart.
    /// </summary>
    private async Task FinishInstalledUpdateAsync()
    {
        LoadingProgressArea.Visibility = Visibility.Collapsed;
        UpdateFailureArea.Visibility   = Visibility.Collapsed;
        UpdateDoneArea.Visibility      = Visibility.Visible;
        TxtUpdateDone.Text             = "Update installiert. Semestria startet gleich neu…";

        await Task.Delay(1200);

        if (AppRelaunch.TryRelaunchAfterUpdate() || AppRelaunch.TryRelaunch())
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }

        UpdateDoneArea.Visibility = Visibility.Collapsed;
        ShowUpdateFailure(
            "Das Update ist installiert. Starte Semestria bitte von Hand neu, "
            + "damit die neue Version läuft.");
    }

    /// <summary>
    /// Waits for a running sync to finish, but carries on once the time limit is up;
    /// a stuck sync must not block the update for good.
    /// </summary>
    private static async Task WaitForSyncToFinishAsync(CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (AppState.IsSyncing && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(250, ct);

        if (AppState.IsSyncing)
            App.LogLine("Update wird trotz laufendem Sync fortgesetzt (Zeitlimit).");
    }

    private void ShowUpdateFailure(string message)
    {
        LoadingProgressArea.Visibility = Visibility.Collapsed;
        UpdatePromptArea.Visibility    = Visibility.Visible;
        UpdateFailureArea.Visibility   = Visibility.Visible;
        TxtUpdateError.Text            = message;

        // After a failure "Später erinnern" is no escape from a mandatory update either;
        // "Semestria beenden" is the way out.
        BtnUpdateNow.Visibility   = Visibility.Collapsed;
        BtnUpdateLater.Visibility = _updateIsMandatory ? Visibility.Collapsed : Visibility.Visible;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End of the Windows session
    // ═══════════════════════════════════════════════════════════════════════

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
    }

    /// <summary>
    /// While installing an update Windows asks the app to close. Hesitating here aborts
    /// the install, so agree immediately and save whatever still needs saving.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmQueryEndSession:
                handled = true;
                return new IntPtr(1);   // yes, go ahead and close us

            case WmEndSession:
                if (wParam != IntPtr.Zero)
                {
                    SaveStateBeforeShutdown();
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Everything the user typed is on disk already, because AppState writes on every
    /// change. What is left here is the config, which may only have been changed in memory.
    /// </summary>
    private void SaveStateBeforeShutdown()
    {
        try
        {
            SchulnetzSync.Core.Configuration.ConfigManager.Save(AppState.Config);
        }
        catch (Exception ex)
        {
            App.LogLine("Zustand vor dem Beenden nicht gesichert: " + ex.Message);
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Start on the dashboard.
        NavView.SelectedItem = NavDashboard;
    }

    /// <summary>
    /// Handles the items that do not open a page. Synapkey lives in the browser, so that
    /// entry does not select itself (SelectsOnInvoked=False) and the sidebar stays put.
    /// </summary>
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem { Tag: "Synapkey" }) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                AppConstants.SynapkeyDashboardUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.LogLine("Synapkey konnte nicht geöffnet werden: " + ex.Message);
            MessageBox.Show(
                "Der Browser konnte nicht geöffnet werden.\n\n" + AppConstants.SynapkeyDashboardUrl,
                "Synapkey", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag as string)
            {
                case "Dashboard": ContentFrame.Navigate(new DashboardPage()); break;
                case "Events":    ContentFrame.Navigate(new EventsPage());    break;
                case "Tasks":     ContentFrame.Navigate(new TasksPage());     break;
                case "Grades":    ContentFrame.Navigate(new GradesPage());    break;
                case "Settings":  ContentFrame.Navigate(new SettingsPage());  break;
                case "About":     ContentFrame.Navigate(new AboutPage());     break;
                case "Help":      ContentFrame.Navigate(new HelpPage());      break;
            }
        }
    }

    /// <summary>Lets another page navigate here, e.g. the settings jumping to the dashboard.</summary>
    public void NavigateTo(string tag)
    {
        switch (tag)
        {
            case "Dashboard": NavView.SelectedItem = NavDashboard; break;
            case "Events":    NavView.SelectedItem = NavEvents;    break;
            case "Tasks":     NavView.SelectedItem = NavTasks;     break;
            case "Grades":    NavView.SelectedItem = NavGrades;    break;
            case "Settings":  NavView.SelectedItem = NavSettings;  break;
            case "About":     NavView.SelectedItem = NavAbout;     break;
            case "Help":      NavView.SelectedItem = NavHelp;      break;
        }
    }
}
