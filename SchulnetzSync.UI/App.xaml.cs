using System.Net.Http;
using System.Threading;
using System.Windows;
using ModernWpf;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Feed;
using SchulnetzSync.UI.Onboarding;

namespace SchulnetzSync.UI;

public partial class App : Application
{
    private TrayService? _tray;

    public App()
    {
        // Explizites Shutdown-Management: verhindert, dass die App schliesst
        // wenn das Onboarding-Fenster geschlossen wird, bevor das Hauptfenster offen ist.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Theme aus Config laden (null = Systemstandard)
        ThemeManager.Current.ApplicationTheme = AppState.Config.ThemePreference switch
        {
            "Light" => (ApplicationTheme?)ApplicationTheme.Light,
            "Dark"  => (ApplicationTheme?)ApplicationTheme.Dark,
            _       => null
        };

        _tray = new TrayService();

        // --silent Modus: Sync im Hintergrund, kein Fenster
        if (e.Args.Contains("--silent"))
        {
            _tray.RunSilentSync();
            // App läuft weiter im Tray; Shutdown via Tray-Menü
            return;
        }

        var config = AppState.Config;

        if (!config.IsOnboardingComplete)
        {
            // Onboarding zeigen; danach Hauptfenster öffnen oder beenden
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
        else
            Shutdown();
    }

    private void OpenMainWindow()
    {
        var main = new MainWindow();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.WindowState = WindowState.Maximized; // Vollbild beim Start
        main.Show();

        // Nach dem ersten Render: Theme fixieren + Feed im Hintergrund laden.
        // ContentRendered kann mehrfach feuern — der Auto-Refresh läuft nur einmal pro Start.
        bool startupRefreshDone = false;
        main.ContentRendered += (_, _) =>
        {
            ForceThemeRefresh();
            if (startupRefreshDone) return;
            startupRefreshDone = true;
            if (AppState.Config.AutoRefreshFeed)
                _ = TryAutoRefreshFeedAsync();
        };
    }

    /// <summary>
    /// Erzwingt eine Theme-Aktualisierung aller DynamicResources.
    /// Nötig weil ModernWPF beim Startup manchmal den Zustand nicht vollständig überträgt.
    /// </summary>
    private static void ForceThemeRefresh()
    {
        var current = ThemeManager.Current.ApplicationTheme;
        // Kurz auf das Gegenteil wechseln, dann zurück — erzwingt Resource-Reload
        ThemeManager.Current.ApplicationTheme =
            current == ApplicationTheme.Light ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.Current.ApplicationTheme = current;
    }

    /// <summary>
    /// Lädt den Feed still im Hintergrund. Kein Fehler im UI wenn offline.
    /// Die URL enthält ein persönliches Token — URL wird NIE geloggt.
    /// </summary>
    private static async Task TryAutoRefreshFeedAsync()
    {
        if (AppState.IsSyncing) return; // Kein paralleler Lauf wenn manueller Sync aktiv

        var plainUrl = ConfigManager.GetFeedUrl(AppState.Config);
        if (string.IsNullOrEmpty(plainUrl)) return;

        // Ladezustand sichtbar machen — das Dashboard zeigt Ring + Statuszeile
        AppState.IsRefreshingFeed = true;
        AppState.Notify();

        try
        {
            using var http   = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var source       = new HttpFeedSource(http, plainUrl);
            var icsContent   = await source.FetchAsync(CancellationToken.None);

            if (AppState.IsSyncing) return; // Nochmals prüfen — manueller Sync hat eventuell begonnen
            var feedEvents = FeedParser.Parse(icsContent);
            AppState.CachedFeedEvents = feedEvents;
            AppState.MarkFeedRefreshed(DateTimeOffset.Now);
        }
        catch
        {
            // Kein Internet, Timeout oder anderer Fehler →
            // gecachte Daten aus der letzten Session weiternutzen (kein UI-Fehler)
        }
        finally
        {
            AppState.IsRefreshingFeed = false;
            AppState.Notify();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
