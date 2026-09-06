using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using SchulnetzSync.Core.Calendar;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.Sync;
using SchulnetzSync.UI.Services;

namespace SchulnetzSync.UI;

/// <summary>
/// Manages the system-tray icon and handles the --silent background sync.
/// The tray icon lets the user open the main window and quit the app.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _trayIcon;

    public TrayService()
    {
        _trayIcon = new NotifyIcon
        {
            Text    = "Semestria",
            Icon    = SystemIcons.Application, // replaced by real icon in production
            Visible = true,
        };

        // Right-click context menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen",  null, (_, _) => ShowMainWindow());
        menu.Items.Add("-");
        menu.Items.Add("Beenden", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;

        // Double-click opens the window
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Called when the app starts with --silent.
    /// Runs a sync in the background; does not open the main window.
    /// </summary>
    public void RunSilentSync()
    {
        _trayIcon.ShowBalloonTip(2000, "Semestria",
            "Synchronisation wird gestartet…", ToolTipIcon.Info);

        // Fire-and-forget on the thread-pool; result shown as balloon tip.
        Task.Run(async () =>
        {
            try
            {
                var result = await SilentSyncCoreAsync();
                ShowBalloon("Synchronisation abgeschlossen", result, ToolTipIcon.Info);
            }
            catch (InteractiveLoginRequiredException)
            {
                ShowBalloon("Anmeldung nötig",
                    "Bitte öffne Semestria und melde dich an.", ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                ShowBalloon("Synchronisation fehlgeschlagen", ex.Message, ToolTipIcon.Error);
            }
        });
    }

    public void Dispose() => _trayIcon.Dispose();

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    private static void ShowMainWindow()
    {
        var win = System.Windows.Application.Current.MainWindow;
        if (win is null) return;
        win.Show();
        win.WindowState = WindowState.Normal;
        win.Activate();
    }

    private static void Shutdown()
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        // Must be called on the UI thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            _trayIcon.ShowBalloonTip(4000, title, text, icon));
    }

    /// <summary>
    /// Runs a full sync using the stored config; returns a human-readable result string.
    /// </summary>
    private static async Task<string> SilentSyncCoreAsync()
    {
        var config = ConfigManager.Load();

        var clientId = MicrosoftAccount.Resolve(config)
            ?? throw new InvalidOperationException("Outlook-Sync ist nicht verfügbar.");

        var plainUrl = ConfigManager.GetFeedUrl(config);
        if (plainUrl is null)
            throw new InvalidOperationException("Keine Feed-URL konfiguriert.");

        // Fetch and parse
        using var http   = new HttpClient();
        var source       = new HttpFeedSource(http, plainUrl);
        var icsContent   = await source.FetchAsync();
        var feedHealth   = FeedParser.CheckPlausibility(icsContent);
        var feedEvents   = FeedParser.Parse(icsContent);

        // Acquire token silently (throws InteractiveLoginRequiredException if expired)
        var auth  = new MsalAuthProvider(clientId);
        var token = await auth.AcquireTokenSilentAsync();

        var calendar = new GraphCalendarTarget(token);
        var options  = config.ToSyncOptions();

        var from    = feedEvents.Count > 0 ? feedEvents.Min(e => e.Start).AddDays(-1) : DateTimeOffset.UtcNow;
        var to      = feedEvents.Count > 0 ? feedEvents.Max(e => e.Start).AddDays(1)  : DateTimeOffset.UtcNow.AddYears(1);
        var tracked = await calendar.GetTrackedEventsAsync(from, to, options.CalendarId);


        var plan = SyncEngine.Build(feedEvents, tracked, options, feedHealth, DateTimeOffset.Now);
        if (!plan.CanExecute)
            throw new InvalidOperationException(string.Join("; ", plan.Blockers));

        if (plan.Actions.Count == 0)
            return "Alles aktuell. Nichts zu tun.";

        await calendar.ExecutePlanAsync(plan, options, progress: null);

        config.LastRunAt     = DateTimeOffset.UtcNow;
        config.LastRunResult = $"{plan.CreateCount} neu, {plan.UpdateCount} aktualisiert, {plan.DeleteCount} gelöscht";
        ConfigManager.Save(config);
        return config.LastRunResult;
    }
}
