using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using SchulnetzSync.Core.Calendar;
using SchulnetzSync.Core.Configuration;

namespace SchulnetzSync.UI.Onboarding;

public partial class OnboardingWindow : Window
{
    private int  _step       = 1;
    private bool _signedIn   = false;
    private bool _skipSignIn = false;

    // Schritte: 1=Willkommen, 2=Rechtliches, 3=Feed-URL, 4=Anmelden, 5=Fertig
    private const int TotalSteps = 5;

    public OnboardingWindow()
    {
        InitializeComponent();

        // Client-ID vorab befüllen (falls schon gesetzt und nicht Platzhalter)
        var existingId = AppState.Config.ClientId;
        if (!string.IsNullOrWhiteSpace(existingId) && IsRealClientId(existingId))
            TxtClientId.Text = existingId;

        UpdateNextButton();
    }

    // -----------------------------------------------------------------------
    // Navigation
    // -----------------------------------------------------------------------

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAdvance()) return;
        if (_step == TotalSteps) { Complete(); return; }
        _step++;
        ShowStep(_step);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_step <= 1) return;
        _step--;
        ShowStep(_step);
    }

    private void BtnSkipSignIn_Click(object sender, RoutedEventArgs e)
    {
        _skipSignIn = true;
        _step++;
        ShowStep(_step);
    }

    private void BtnOpenAzure_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade")
            {
                UseShellExecute = true
            });
        }
        catch { /* Browser nicht verfügbar — kein Problem */ }
    }

    private void ShowStep(int step)
    {
        Step1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        SetDot(Dot1, active: step == 1, done: step > 1);
        SetDot(Dot2, active: step == 2, done: step > 2);
        SetDot(Dot3, active: step == 3, done: step > 3);
        SetDot(Dot4, active: step == 4, done: step > 4);
        SetDot(Dot5, active: step == 5, done: false);

        BtnBack.IsEnabled = step > 1;
        BtnNext.Content   = step == TotalSteps ? "Los geht's!" : "Weiter";

        if (step == 4) OnEnterStep4();
        if (step == 5) BuildFinishSummary();

        UpdateNextButton();
    }

    private void SetDot(System.Windows.Shapes.Ellipse dot, bool active, bool done)
    {
        dot.Width  = active ? 10 : 8;
        dot.Height = active ? 10 : 8;
        dot.Fill   = (active || done)
            ? (Brush)new SolidColorBrush(Color.FromRgb(0x5C, 0x6E, 0xF7))
            : (Brush)Application.Current.FindResource("SystemControlForegroundBaseLowBrush");
    }

    // -----------------------------------------------------------------------
    // Schritt-Validierung
    // -----------------------------------------------------------------------

    private bool CanAdvance() => _step switch
    {
        2 => ValidateLegal(),
        3 => ValidateFeedUrl(),
        _ => true
    };

    private bool ValidateFeedUrl()
    {
        var url = TxtFeedUrl.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            TxtFeedStatus.Text    = "Bitte eine Feed-URL eingeben.";
            TxtFeedStatus.Opacity = 1;
            return false;
        }
        if (!url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
         && !url.StartsWith("https://",  StringComparison.OrdinalIgnoreCase)
         && !url.StartsWith("http://",   StringComparison.OrdinalIgnoreCase))
        {
            TxtFeedStatus.Text    = "URL muss mit webcal://, https:// oder http:// beginnen.";
            TxtFeedStatus.Opacity = 1;
            return false;
        }
        return true;
    }

    private bool ValidateLegal()
    {
        if (ChkAgb.IsChecked != true || ChkDatenschutz.IsChecked != true)
        {
            MessageBox.Show(
                "Bitte akzeptiere beide Dokumente um fortzufahren.",
                "Zustimmung erforderlich",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    // -----------------------------------------------------------------------
    // Schritt 2 — Rechtliches
    // -----------------------------------------------------------------------

    private void Legal_CheckChanged(object sender, RoutedEventArgs e)
        => UpdateNextButton();

    // -----------------------------------------------------------------------
    // Schritt 3 — Feed-URL
    // -----------------------------------------------------------------------

    private void TxtFeedUrl_TextChanged(object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        TxtFeedStatus.Text    = "🔒  Die URL wird verschlüsselt gespeichert. Das Token verlässt dein Gerät nie.";
        TxtFeedStatus.Opacity = 0.55;
        UpdateNextButton();
    }

    // -----------------------------------------------------------------------
    // Schritt 4 — Microsoft-Anmeldung
    // -----------------------------------------------------------------------

    private void OnEnterStep4()
    {
        bool hasRealId = IsRealClientId(TxtClientId.Text);
        ClientIdHint.Visibility  = hasRealId ? Visibility.Collapsed : Visibility.Visible;
        BtnSignIn.IsEnabled      = hasRealId && !_signedIn;
        SignInSuccess.Visibility  = _signedIn ? Visibility.Visible : Visibility.Collapsed;
        TxtSignInError.Visibility = Visibility.Collapsed;
    }

    private void TxtClientId_TextChanged(object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        bool hasRealId = IsRealClientId(TxtClientId.Text);
        ClientIdHint.Visibility = hasRealId ? Visibility.Collapsed : Visibility.Visible;
        BtnSignIn.IsEnabled     = hasRealId && !_signedIn;
        UpdateNextButton();
    }

    private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
    {
        var clientId = TxtClientId.Text.Trim();
        if (!IsRealClientId(clientId)) return;

        BtnSignIn.IsEnabled       = false;
        TxtSignInError.Visibility = Visibility.Collapsed;

        try
        {
            // Frische Instanz bei jedem Versuch — kein eingefrierter Zustand
            var auth = new MsalAuthProvider(clientId);
            await auth.AcquireTokenInteractiveAsync();

            _signedIn = true;
            SignInSuccess.Visibility = Visibility.Visible;
            BtnSignIn.Visibility     = Visibility.Collapsed;
            AppState.Config.ClientId = clientId;

            UpdateNextButton();
        }
        catch (Exception ex)
        {
            // Fehlermeldung OHNE Client-ID oder URL
            var safeMsg = SanitizeErrorMessage(ex.Message, clientId);
            TxtSignInError.Text       = "Fehler: " + safeMsg;
            TxtSignInError.Visibility = Visibility.Visible;
            // Button aktivieren: erneuter Versuch möglich
            BtnSignIn.IsEnabled = true;
        }
    }

    // -----------------------------------------------------------------------
    // Schritt 5 — Fertig
    // -----------------------------------------------------------------------

    private void BuildFinishSummary()
    {
        var url     = TxtFeedUrl.Text.Trim();
        var safeUrl = SafeDisplayUrl(url);

        TxtSetupSummary.Text =
            $"✅  Feed-URL: {safeUrl}\n" +
            (_signedIn
                ? "✅  Microsoft-Konto: angemeldet\n"
                : "Hinweis: Microsoft-Konto noch nicht eingerichtet (in den Einstellungen nachholen)\n") +
            "✅  Nutzungsbedingungen akzeptiert\n" +
            "✅  Datenschutzerklärung akzeptiert\n\n" +
            "Klicke «Los geht's!» um das Hauptfenster zu öffnen.";
    }

    // -----------------------------------------------------------------------
    // Abschluss
    // -----------------------------------------------------------------------

    private void Complete()
    {
        var config = AppState.Config;

        // Feed-URL verschlüsselt speichern
        var url = TxtFeedUrl.Text.Trim();
        if (!string.IsNullOrEmpty(url))
            ConfigManager.SetFeedUrl(config, url);

        // Client-ID speichern (falls eingegeben)
        var clientId = TxtClientId.Text.Trim();
        if (IsRealClientId(clientId))
            config.ClientId = clientId;

        config.IsOnboardingComplete = true;
        config.AcceptedLegalVersion = AppConstants.LegalVersion;
        ConfigManager.Save(config);
        AppState.Reload();

        // Fenster schliessen; App.xaml.cs öffnet danach das Hauptfenster
        Close();
    }

    // -----------------------------------------------------------------------
    // Button-Aktivierung
    // -----------------------------------------------------------------------

    private void UpdateNextButton()
    {
        BtnNext.IsEnabled = _step switch
        {
            2 => ChkAgb.IsChecked == true && ChkDatenschutz.IsChecked == true,
            3 => !string.IsNullOrWhiteSpace(TxtFeedUrl.Text),
            4 => _signedIn || _skipSignIn,  // Überspringen ist erlaubt
            _ => true
        };
    }

    // -----------------------------------------------------------------------
    // Security-Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Gibt true zurück wenn die Client-ID wie eine echte GUID aussieht.
    /// Schützt davor, dass der Platzhalter «YOUR-CLIENT-ID-HERE» verwendet wird.
    /// </summary>
    private static bool IsRealClientId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Contains("YOUR",         StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("PLACEHOLDER",  StringComparison.OrdinalIgnoreCase)) return false;
        return id.Length >= 32 && id.Contains('-');
    }

    /// <summary>
    /// Zeigt von einer Feed-URL nur Protokoll+Host+Pfad — KEIN Token/Query-String.
    /// Schützt vor unabsichtlichem Anzeigen des persönlichen Tokens.
    /// </summary>
    private static string SafeDisplayUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "(nicht gesetzt)";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.GetLeftPart(UriPartial.Path) + "  [Token ausgeblendet]";
        return url.Length > 40 ? url[..40] + "..." : url;
    }

    /// <summary>
    /// Bereinigt Exception-Meldungen: entfernt Client-IDs und URLs damit
    /// diese nicht in der UI erscheinen.
    /// </summary>
    private static string SanitizeErrorMessage(string message, string? clientId)
    {
        if (string.IsNullOrEmpty(message)) return "Unbekannter Fehler";

        if (!string.IsNullOrEmpty(clientId))
            message = message.Replace(clientId, "[App-ID]", StringComparison.OrdinalIgnoreCase);

        message = System.Text.RegularExpressions.Regex.Replace(
            message, @"https?://\S+", "[URL]");

        if (message.Contains("AADSTS700016"))
            return "App-ID nicht gefunden. Bitte überprüfe die Client-ID auf portal.azure.com.";
        if (message.Contains("AADSTS65004"))
            return "Zugriff verweigert. Hast du Calendars.ReadWrite in der App-Registrierung aktiviert?";
        if (message.Contains("AADSTS50034") || message.Contains("AADSTS50020"))
            return "Microsoft-Konto nicht gefunden. Überprüfe deine Anmeldedaten.";
        if (message.Contains("canceled") || message.Contains("aborted")
            || message.Contains("abgebrochen"))
            return "Anmeldung abgebrochen. Klicke erneut auf «Anmelden» um es zu versuchen.";

        var firstLine = message.Split('\n')[0].Trim();
        return firstLine.Length > 120 ? firstLine[..120] + "..." : firstLine;
    }
}
