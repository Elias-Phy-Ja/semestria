using System.Net.Http;
using System.Threading;
using System.Windows;
using ModernWpf;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.News;
using SchulnetzSync.UI.News;
using SchulnetzSync.UI.Onboarding;
using SchulnetzSync.UI.Update;

namespace SchulnetzSync.UI;

/// <summary>
/// Everything that happens around the app itself: command line, theme, onboarding, the
/// tray icon, the background feed refresh and the crash log.
///
/// Three ways in: --silent stays in the tray and syncs, a first start runs the onboarding
/// wizard, and everything else goes straight to the main window.
/// </summary>
public partial class App : Application
{
    private TrayService? _tray;

    /// <summary>True when Windows restarted the app after installing an update.</summary>
    private bool _restarted;

    /// <summary>Where update information comes from; a stand-in when --fake-update is set.</summary>
    private IUpdateSourceFactory _updateSources = new StoreUpdateSourceFactory();

    /// <summary>--fake-news: sample messages instead of the real news.json, seen ids ignored.</summary>
    private static bool _fakeNews;
    private System.Windows.Threading.DispatcherTimer? _reminderTimer;

    public App()
    {
        // Shut down only when we say so. Otherwise closing the onboarding window would
        // end the app before the main window has even opened.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Profiles first: every file path below depends on which person is running.
        ProfileSession.Initialize();
        ProfileSession.CleanUpRemovedProfiles();

        // Show unhandled errors instead of letting the app vanish without a word.
        DispatcherUnhandledException += OnUnhandledException;

        // Proportional wheel and touchpad scrolling, see SmoothScroll.
        SmoothScroll.Install();

        _restarted = e.Args.Contains(ApplicationRestart.RestartedArgument,
                                     StringComparer.OrdinalIgnoreCase);

        // Outside a Store install the real check always says "nothing to do". The fake
        // source is what makes the four states of the loading view testable anyway.
        if (e.Args.Contains("--fake-update-mandatory", StringComparer.OrdinalIgnoreCase))
            _updateSources = new FakeUpdateSourceFactory("2.1.0.0", mandatory: true);
        else if (e.Args.Contains("--fake-update", StringComparer.OrdinalIgnoreCase))
            _updateSources = new FakeUpdateSourceFactory("2.1.0.0", mandatory: false);

        // Same idea for the news: shows sample messages without the server
        if (e.Args.Contains("--fake-news", StringComparer.OrdinalIgnoreCase))
            _fakeNews = true;

        // Theme from the config; with no choice of their own the app starts dark.
        ThemeManager.Current.ApplicationTheme = AppState.Config.ResolveTheme() switch
        {
            "Light" => (ApplicationTheme?)ApplicationTheme.Light,
            "Dark"  => (ApplicationTheme?)ApplicationTheme.Dark,
            _       => null
        };

        // Changed legal texts come before everything else: no window, no update check, no
        // feed, no sync until they are accepted.
        if (LegalAcceptWindow.IsNeeded(AppState.Config))
        {
            // A background start cannot ask, so it does nothing at all; the texts appear the
            // next time someone opens the app.
            if (e.Args.Contains("--silent")) { Shutdown(); return; }

            var legal = new LegalAcceptWindow();
            legal.ShowDialog();
            if (!legal.Accepted) { Shutdown(); return; }
        }

        _tray = new TrayService();

        // --silent: sync in the background, never show a window.
        if (e.Args.Contains("--silent"))
        {
            _tray.RunSilentSync();
            // Stays alive in the tray; quitting goes through the tray menu.
            return;
        }

        var config = AppState.Config;

        if (!config.IsOnboardingComplete)
        {
            // First start: run the wizard, then open the window or give up.
            var onboarding = new OnboardingWindow();
            onboarding.Closed += OnOnboardingClosed;
            MainWindow = onboarding;
            onboarding.Show();
        }
        else
        {
            OpenMainWindow();
        }
    }

    private void OnOnboardingClosed(object? sender, EventArgs e)
    {
        if (AppState.Config.IsOnboardingComplete)
            OpenMainWindow();
        else if (!ProfileSession.AbandonUnfinishedProfile())   // a new profile goes back to the previous one
            Shutdown();
    }

    private void OpenMainWindow()
    {
        // Counts the starts with a window; the news stay away for the first few
        var config = AppState.Config;
        config.StartCount++;
        ConfigManager.Save(config);

        var main = new MainWindow();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.WindowState = WindowState.Maximized; // a calendar wants the whole screen
        main.Show();

        // Sync and reminders only start once the loading view is gone. An update can still
        // be pending during that phase, and a half-written calendar is the worst possible
        // outcome of one.
        main.StartupFinished += () =>
        {
            if (AppState.Config.AutoRefreshFeed)
                _ = TryAutoRefreshFeedAsync();
            StartReminderTimer();
            _ = ShowNewsLaterAsync(main);
        };

        // ContentRendered can fire more than once; the startup phase must not.
        bool startupDone = false;
        main.ContentRendered += async (_, _) =>
        {
            ForceThemeRefresh();
            if (startupDone) return;
            startupDone = true;

            await main.RunStartupAsync(_restarted, _updateSources);
        };
    }

    /// <summary>
    /// Shows unhandled errors instead of letting the app disappear without a word, and
    /// writes them to %LOCALAPPDATA%\Semestria\crash.log. The app keeps running: one page
    /// that failed to build should not cost the whole session.
    /// </summary>
    private void OnUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var ex = e.Exception;

        // TargetInvocationException and friends hide the real cause, so log the whole
        // chain rather than just the outermost wrapper.
        var sb = new System.Text.StringBuilder();
        sb.Append(DateTimeOffset.Now.ToString("u")).Append('\n');
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            sb.Append(current == ex ? "" : "--- InnerException ---\n")
              .Append(current.GetType().FullName).Append(": ").Append(current.Message).Append('\n')
              .Append(current.StackTrace).Append('\n');
        }
        sb.Append('\n');
        var text = sb.ToString();

        // The dialog gets the innermost message, which is the one that says anything.
        var root = ex;
        while (root.InnerException is not null) root = root.InnerException;

        AppendToLog(text);

        MessageBox.Show(
            root.Message + "\n\nDetails stehen in %LOCALAPPDATA%\\Semestria\\crash.log.",
            "Es ist ein Fehler aufgetreten",
            MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }

    /// <summary>
    /// Writes one line to %LOCALAPPDATA%\Semestria\crash.log.
    ///
    /// For things the user should not be bothered with but that still have to be traceable
    /// afterwards — an update check that quietly ran into an offline machine, for instance.
    /// </summary>
    public static void LogLine(string message)
        => AppendToLog($"{DateTimeOffset.Now:u}  {message}\n");

    private static void AppendToLog(string text)
    {
        try
        {
            // One log for the whole computer, not per profile
            System.IO.Directory.CreateDirectory(SchulnetzSync.Core.Profiles.DataPaths.Root);
            System.IO.File.AppendAllText(SchulnetzSync.Core.Profiles.DataPaths.GlobalFile("crash.log"), text);
        }
        catch { /* Protokollieren darf nie den Ablauf stören */ }
    }

    /// <summary>
    /// Checks once a minute whether a task reminder is due, and once right away so that
    /// anything that came due while the app was closed is caught up at start.
    /// </summary>
    private void StartReminderTimer()
    {
        _tray?.ShowDueTaskReminders();

        _reminderTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _reminderTimer.Tick += (_, _) => _tray?.ShowDueTaskReminders();
        _reminderTimer.Start();
    }

    /// <summary>
    /// Forces every DynamicResource to re-evaluate the theme. Needed because ModernWPF
    /// sometimes does not carry the state over completely at startup.
    /// </summary>
    private static void ForceThemeRefresh()
    {
        var current = ThemeManager.Current.ApplicationTheme;
        // Flip to the opposite theme and straight back; that is what triggers the reload.
        ThemeManager.Current.ApplicationTheme =
            current == ApplicationTheme.Light ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.Current.ApplicationTheme = current;
    }

    /// <summary>
    /// Pulls the feed quietly in the background. Offline is not an error worth showing.
    /// The URL carries a personal token and is NEVER logged.
    /// </summary>
    private static async Task TryAutoRefreshFeedAsync()
    {
        if (AppState.IsSyncing) return; // a manual sync is already running

        var plainUrl = ConfigManager.GetFeedUrl(AppState.Config);
        if (string.IsNullOrEmpty(plainUrl)) return;

        // Make the loading visible: the dashboard shows a ring and a status line.
        AppState.IsRefreshingFeed = true;
        AppState.Notify();

        try
        {
            using var http   = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var source       = new HttpFeedSource(http, plainUrl);
            var icsContent   = await source.FetchAsync(CancellationToken.None);

            if (AppState.IsSyncing) return; // check again, a manual sync may have started meanwhile
            var feedEvents = FeedParser.Parse(icsContent);
            AppState.ApplyFeed(feedEvents, FeedParser.CheckPlausibility(icsContent));
            AppState.MarkFeedRefreshed(DateTimeOffset.Now);
        }
        catch
        {
            // No connection, a timeout, anything at all: keep using the cached events from
            // the last session. An offline start is normal, not a failure.
        }
        finally
        {
            AppState.IsRefreshingFeed = false;
            AppState.Notify();
        }
    }

    /// <summary>
    /// Some seconds after the Store check, loads news.json and shows what is new. Runs late
    /// on purpose so it never competes with the update check or the first paint.
    /// Offline or a broken file is logged, never shown.
    /// </summary>
    /// <summary>
    /// The first starts are for finding one's way around: no developer news before the
    /// fourth start, not even quietly in the background.
    /// </summary>
    private const int QuietStarts = 3;

    private static async Task ShowNewsLaterAsync(Window owner)
    {
        if (!AppState.Config.ShowNews) return;
        if (!_fakeNews && AppState.Config.StartCount <= QuietStarts) return;

        await Task.Delay(TimeSpan.FromSeconds(10));
        if (!AppState.Config.ShowNews || !owner.IsVisible) return;

        IReadOnlyList<NewsItem> pending;
        try
        {
            using var http = new HttpClient();
            INewsSource source = _fakeNews ? new FakeNewsSource() : new HttpNewsSource(http);
            var feed = await source.FetchAsync();
            var seen = _fakeNews ? new HashSet<string>() : AppState.SeenNewsIds;
            pending  = NewsSelector.Pending(feed, seen, DateTimeOffset.Now, AppConstants.Version);
        }
        catch (Exception ex)
        {
            LogLine($"Nachrichten nicht geladen: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (pending.Count == 0 || !owner.IsVisible) return;
        new NewsWindow(pending) { Owner = owner }.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _reminderTimer?.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
