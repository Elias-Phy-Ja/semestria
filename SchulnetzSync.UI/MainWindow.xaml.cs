using System.Windows;
using System.Windows.Interop;
using ModernWpf.Controls;
using SchulnetzSync.UI.Pages;
using SchulnetzSync.UI.Update;

namespace SchulnetzSync.UI;

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

    /// <summary>Runs once the loading view is gone.</summary>
    public event Action? StartupFinished;

    public MainWindow()
    {
        InitializeComponent();
        TxtLoadingVersion.Text = "Version " + AppConstants.Version;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ladephase
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs the loading view: config, update check, initialisation.
    /// </summary>
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
            return;   // Fenster wird geschlossen
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
                break;
        }
    }

    /// <summary>Kurze Bestätigung nach einem Neustart, dann weiter.</summary>
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

        LoadingProgressArea.Visibility = Visibility.Collapsed;
        UpdateFailureArea.Visibility   = Visibility.Collapsed;
        UpdatePromptArea.Visibility    = Visibility.Visible;

        TxtUpdateHeadline.Text = version is null
            ? "Ein Update ist verfügbar"
            : $"Version {version} verfügbar";

        TxtUpdateBody.Text = mandatory
            ? $"Du hast {AppConstants.Version}. Dieses Update ist erforderlich, "
              + "Semestria kann ohne es nicht fortfahren."
            : $"Du hast {AppConstants.Version}. Das Update kommt aus dem Microsoft Store.";

        // Bei einem zwingenden Update gibt es keinen Weg in die App.
        BtnUpdateLater.Visibility = mandatory ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FinishStartup()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        StartupFinished?.Invoke();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Update-Knöpfe
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

            // Ein laufender Kalender-Sync darf nicht mitten im Schreiben
            // abgewürgt werden, sonst bleiben halbe Einträge in Outlook zurück.
            await WaitForSyncToFinishAsync(ct);

            // Muss vor der Installation stehen: danach beendet Windows die App.
            if (!ApplicationRestart.Register())
                App.LogLine("Automatischer Neustart konnte nicht registriert werden.");

            await _startup.InstallAsync(ct);

            // Hierhin gelangt man nur, wenn Windows die App nicht beendet hat.
            ShowUpdateFailure("Die Installation wurde nicht durchgeführt.");
        }
        catch (OperationCanceledException)
        {
            // Fenster wird geschlossen — nichts weiter zu tun.
        }
        catch (Exception ex)
        {
            App.LogLine("Update fehlgeschlagen: " + ex);
            ShowUpdateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Wartet, bis ein laufender Sync fertig ist. Nach dem Zeitlimit wird
    /// trotzdem fortgefahren — ein hängender Sync darf das Update nicht
    /// dauerhaft blockieren.
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

        // Nach einem Fehlschlag ist "Später erinnern" auch bei einem zwingenden
        // Update kein Ausweg — dafür gibt es "Semestria beenden".
        BtnUpdateNow.Visibility   = Visibility.Collapsed;
        BtnUpdateLater.Visibility = _updateIsMandatory ? Visibility.Collapsed : Visibility.Visible;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sitzungsende
    // ═══════════════════════════════════════════════════════════════════════

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
    }

    /// <summary>
    /// Beim Einspielen eines Updates fordert Windows die App zum Beenden auf.
    /// Hängt sie hier, bricht die Installation ab — darum sofort zustimmen und
    /// den Zustand sichern.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmQueryEndSession:
                handled = true;
                return new IntPtr(1);   // ja, wir können beendet werden

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
    /// Alles, was der Nutzer eingegeben hat, liegt bereits auf der Platte:
    /// AppState schreibt bei jeder Änderung. Hier bleibt das Speichern der
    /// Konfiguration, die nur im Speicher verändert worden sein könnte.
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
        // Dashboard beim Start auswählen
        NavView.SelectedItem = NavDashboard;
    }

    /// <summary>
    /// Handles items that do not open a page. Synapkey lebt im Browser —
    /// das Element wählt sich darum nicht selbst aus (SelectsOnInvoked=False).
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
                case "Settings":  ContentFrame.Navigate(new SettingsPage());  break;
                case "About":     ContentFrame.Navigate(new AboutPage());     break;
            }
        }
    }

    /// <summary>Navigiert von aussen auf eine bestimmte Seite (z.B. aus Settings heraus).</summary>
    public void NavigateTo(string tag)
    {
        switch (tag)
        {
            case "Dashboard": NavView.SelectedItem = NavDashboard; break;
            case "Events":    NavView.SelectedItem = NavEvents;    break;
            case "Tasks":     NavView.SelectedItem = NavTasks;     break;
            case "Settings":  NavView.SelectedItem = NavSettings;  break;
        }
    }
}
