using System.Windows;
using ModernWpf;
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
