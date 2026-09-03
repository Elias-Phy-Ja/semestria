using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.UI.Services;

namespace SchulnetzSync.UI.Pages;

public partial class DashboardPage : Page
{
    private readonly SyncService _sync = new();
    private CancellationTokenSource? _cts;

    public DashboardPage()
    {
        InitializeComponent();
        _sync.ProgressReceived += msg => Dispatcher.Invoke(() => AppendLog(msg));
        _sync.Completed        += result => Dispatcher.Invoke(() => OnSyncCompleted(result));
        _sync.Failed           += ex => Dispatcher.Invoke(() =>
        {
            AppendLog("❌ " + ex.Message);
            SetBusy(false);
        });

        Loaded   += (_, _) => { AppState.Changed += RefreshStatus; RefreshStatus(); };
        Unloaded += (_, _) => AppState.Changed -= RefreshStatus;
    }

    // -----------------------------------------------------------------------
    // Buttons
    // -----------------------------------------------------------------------

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
        => await StartSyncAsync(dryRun: false);

    private async void BtnDryRun_Click(object sender, RoutedEventArgs e)
        => await StartSyncAsync(dryRun: true);

    private void BtnGoSettings_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main)
            main.NavigateTo("Settings");
    }

    // -----------------------------------------------------------------------
    // Sync
    // -----------------------------------------------------------------------

    private async Task StartSyncAsync(bool dryRun)
    {
        if (AppState.IsSyncing) return;
        TxtLog.Text = dryRun ? "👁  Vorschau wird berechnet…" : "⏳ Synchronisation startet…";
        SetBusy(true);
        _cts = new CancellationTokenSource();
        await _sync.RunAsync(dryRun, _cts.Token);
    }

    private void OnSyncCompleted(SyncResult result)
    {
        SetBusy(false);
        RefreshStatus();
    }

    // -----------------------------------------------------------------------
    // UI-Helpers
    // -----------------------------------------------------------------------

    private void RefreshStatus()
    {
        var config = AppState.Config;
        bool hasFeed = ConfigManager.GetFeedUrl(config) is not null;
        SetupHint.Visibility = hasFeed ? Visibility.Collapsed : Visibility.Visible;
        BtnSync.IsEnabled    = hasFeed;
        BtnDryRun.IsEnabled  = hasFeed;

        if (config.LastRunAt.HasValue)
        {
            var ago = DateTimeOffset.Now - config.LastRunAt.Value;
            TxtLastRun.Text = $"Letzter Lauf: {FormatAgo(ago)}";
            TxtLastResult.Text = config.LastRunResult ?? "";

            bool fresh = ago.TotalHours < 25;
            StatusDot.Fill  = new SolidColorBrush(fresh ? Color.FromRgb(0x22,0xC5,0x5E)
                                                        : Color.FromRgb(0xF5,0x9E,0x0B));
            TxtStatusLabel.Text = fresh ? "Synchronisation aktuell" : "Sync überfällig";
            TxtStatStatus.Text  = fresh ? "✅" : "⚠️";
            TxtStatDetail.Text  = FormatAgo(ago) + " ago";
        }
        else
        {
            TxtLastRun.Text     = "Noch nie synchronisiert";
            TxtLastResult.Text  = "";
            StatusDot.Fill      = new SolidColorBrush(Color.FromRgb(0xF5,0x9E,0x0B));
            TxtStatusLabel.Text = "Noch kein Sync";
            TxtStatStatus.Text  = "⏳";
            TxtStatDetail.Text  = "Noch kein Lauf";
        }

        var cached = AppState.CachedFeedEvents;
        if (cached.Count > 0)
        {
            TxtStatPruefung.Text = cached.Count(e => e.Type == SchulnetzSync.Core.Model.SchulnetzEventType.Pruefung).ToString();
            TxtStatTermin.Text   = cached.Count(e => e.Type == SchulnetzSync.Core.Model.SchulnetzEventType.Termin).ToString();
        }
        else
        {
            TxtStatPruefung.Text = "·";
            TxtStatTermin.Text   = "·";
        }
        TxtHeaderSub.Text = hasFeed ? "Alles im Blick." : "Fast fertig. Feed-URL in den Einstellungen eintragen.";
    }

    private void AppendLog(string line)
    {
        var current = TxtLog.Text;
        var isInit  = current.StartsWith("Bereit") || current.StartsWith("👁") || current.StartsWith("⏳");
        TxtLog.Text = isInit ? line : current + "\n" + line;
        LogScroll.ScrollToBottom();
    }

    private void SetBusy(bool busy)
    {
        SyncRing.IsActive   = busy;
        BtnSync.IsEnabled   = !busy;
        BtnDryRun.IsEnabled = !busy;
    }

    private static string FormatAgo(TimeSpan ts)
    {
        if (ts.TotalMinutes < 2)  return "gerade eben";
        if (ts.TotalMinutes < 60) return $"vor {(int)ts.TotalMinutes} Min.";
        if (ts.TotalHours   < 24) return $"vor {(int)ts.TotalHours} Std.";
        return $"vor {(int)ts.TotalDays} Tag(en)";
    }
}
