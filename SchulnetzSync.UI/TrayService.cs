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
/// The tray icon and everything that hangs off it: opening the window, quitting, the
/// --silent background sync and the balloon tips for due task reminders.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _trayIcon;

    public TrayService()
    {
        _trayIcon = new NotifyIcon
        {
            Text    = "Semestria",
            Icon    = SystemIcons.Application, // swapped for the real icon in the packaged build
            Visible = true,
        };

        // Right-click menu: open and quit, nothing more.
        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen",  null, (_, _) => ShowMainWindow());
        menu.Items.Add("-");
        menu.Items.Add("Beenden", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;

        // Double-click does the obvious thing.
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    // ── What the rest of the app calls ──────────────────────────────────

    /// <summary>
    /// Entry point for a start with --silent: sync in the background and never
    /// put a window on screen.
    /// </summary>
    public void RunSilentSync()
    {
        _trayIcon.ShowBalloonTip(2000, "Semestria",
            "Synchronisation wird gestartet…", ToolTipIcon.Info);

        // Fire and forget on the thread pool; whatever comes back turns into a balloon tip.
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

    /// <summary>
    /// Shows due task reminders as balloon tips and marks them as shown, so they do not
    /// come back on the next tick. Driven by a timer in the main window.
    /// </summary>
    public void ShowDueTaskReminders()
    {
        var now = DateTimeOffset.Now;
        var due = AppState.Tasks.Where(t => t.ReminderPending(now)).ToList();
        if (due.Count == 0) return;

        var text = due.Count == 1
            ? BuildReminderLine(due[0])
            : string.Join("\n", due.Take(3).Select(BuildReminderLine))
              + (due.Count > 3 ? $"\n… und {due.Count - 3} weitere" : "");

        ShowBalloon(due.Count == 1 ? "Erinnerung" : $"{due.Count} Erinnerungen",
            text, ToolTipIcon.Info);

        foreach (var task in due)
            AppState.UpdateTask(task with { ReminderShown = true });
    }

    private static string BuildReminderLine(SchulnetzSync.UI.Model.TaskItem task)
    {
        var list = string.IsNullOrWhiteSpace(task.ListName) ? "" : task.ListName + ": ";
        if (task.DueAt is not { } due) return list + task.Title;

        int days = (due.Date - DateTime.Today).Days;
        var when = days switch
        {
            < 0 => "überfällig",
            0   => "heute fällig",
            1   => "morgen fällig",
            _   => $"in {days} Tagen fällig",
        };
        return $"{list}{task.Title} — {when}";
    }

    public void Dispose() => _trayIcon.Dispose();

    // ── Internals ───────────────────────────────────────────────────────

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
        // Has to run on the UI thread.
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            _trayIcon.ShowBalloonTip(4000, title, text, icon));
    }

    /// <summary>
    /// A full sync from the stored config, returning the line that goes into the balloon tip.
    /// </summary>
    private static async Task<string> SilentSyncCoreAsync()
    {
        var config = ConfigManager.Load();

        var clientId = MicrosoftAccount.Resolve(config)
            ?? throw new InvalidOperationException("Outlook-Sync ist nicht verfügbar.");

        var plainUrl = ConfigManager.GetFeedUrl(config);
        if (plainUrl is null)
            throw new InvalidOperationException("Keine Feed-URL konfiguriert.");

        // Feed first — no point asking Microsoft for a token if this fails.
        using var http   = new HttpClient();
        var source       = new HttpFeedSource(http, plainUrl);
        var icsContent   = await source.FetchAsync();
        var feedHealth   = FeedParser.CheckPlausibility(icsContent);
        var feedEvents   = FeedParser.Parse(icsContent);

        // Silent only: if the token is gone this throws, and the caller turns it into a hint.
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
