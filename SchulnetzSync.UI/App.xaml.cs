using System.Net.Http;
using System.Threading;
using System.Windows;
using ModernWpf;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Feed;
using SchulnetzSync.UI.Onboarding;
using SchulnetzSync.UI.Update;

namespace SchulnetzSync.UI;

public partial class App : Application
{
    private TrayService? _tray;

    /// <summary>True when Windows restarted the app after installing an update.</summary>
    private bool _restarted;

    /// <summary>Where update information comes from; an attrappe with --fake-update.</summary>
    private IUpdateSourceFactory _updateSources = new StoreUpdateSourceFactory();
    private System.Windows.Threading.DispatcherTimer? _reminderTimer;

    public App()
    {
        // Explizites Shutdown-Management: verhindert, dass die App schliesst
        // wenn das Onboarding-Fenster geschlossen wird, bevor das Hauptfenster offen ist.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Unbehandelte Fehler sichtbar machen statt die App stumm beenden zu lassen
        DispatcherUnhandledException += OnUnhandledException;

        // Rad/Touchpad proportional scrollen lassen (siehe SmoothScroll)
        SmoothScroll.Install();

        _restarted = e.Args.Contains(ApplicationRestart.RestartedArgument,
                                     StringComparer.OrdinalIgnoreCase);

        // Ohne Store-Installation liefert die echte Prüfung immer «kein Update».
        // Die Attrappe macht die vier Zustände der Ladeansicht trotzdem prüfbar.
        if (e.Args.Contains("--fake-update-mandatory", StringComparer.OrdinalIgnoreCase))
            _updateSources = new FakeUpdateSourceFactory("2.1.0.0", mandatory: true);
        else if (e.Args.Contains("--fake-update", StringComparer.OrdinalIgnoreCase))
            _updateSources = new FakeUpdateSourceFactory("2.1.0.0", mandatory: false);

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

        // Sync und Erinnerungen starten erst, wenn die Ladeansicht weg ist:
        // Während der Ladephase kann ein Update anstehen, und ein halb
        // geschriebener Kalender wäre das schlechteste Ergebnis davon.
        main.StartupFinished += () =>
        {
            if (AppState.Config.AutoRefreshFeed)
                _ = TryAutoRefreshFeedAsync();
            StartReminderTimer();
        };

        // ContentRendered kann mehrfach feuern — die Ladephase läuft einmal.
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
    /// Zeigt unbehandelte Fehler an, statt die App wortlos beenden zu lassen.
    /// Die Meldung wird zusätzlich nach %LOCALAPPDATA%\Semestria\crash.log
    /// geschrieben. Die App läuft weiter — ein Fehler beim Aufbau einer Seite
    /// soll nicht die ganze Sitzung kosten.
    /// </summary>
    private void OnUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var ex = e.Exception;

        // TargetInvocationException & Co. verbergen die eigentliche Ursache —
        // darum die ganze Kette protokollieren, nicht nur die äusserste Hülle.
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

        // Für den Dialog die innerste Meldung — sie beschreibt das echte Problem.
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
    /// Für Dinge, die der Nutzer nicht sehen soll, aber nachvollziehbar bleiben
    /// müssen — etwa eine Update-Prüfung, die offline ins Leere lief.
    /// </summary>
    public static void LogLine(string message)
        => AppendToLog($"{DateTimeOffset.Now:u}  {message}\n");

    private static void AppendToLog(string text)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Semestria");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"), text);
        }
        catch { /* Protokollieren darf nie den Ablauf stören */ }
    }

    /// <summary>
    /// Prüft einmal pro Minute, ob eine Aufgaben-Erinnerung fällig ist.
    /// Einmal sofort, damit Erinnerungen aus der Zeit ohne laufende App
    /// beim Start nachgeholt werden.
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
        _reminderTimer?.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
