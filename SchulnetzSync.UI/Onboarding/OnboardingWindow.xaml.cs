using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using SchulnetzSync.Core.Calendar;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.UI.Services;

namespace SchulnetzSync.UI.Onboarding;

public partial class OnboardingWindow : Window
{
    private int  _step       = 1;
    private bool _signedIn   = false;
    private bool _skipSignIn = false;

    /// <summary>
    /// Schritt 4 ist zweigeteilt: erst die Frage, dann - nur bei Ja - die
    /// eigentliche Einrichtung. Die Einrichtung ist der mit Abstand
    /// komplizierteste Teil und soll niemanden aufhalten, der sie nicht braucht.
    /// </summary>
    private bool _showOutlookSetup;

    // Schritte: 1=Willkommen, 2=Rechtliches, 3=Feed-URL, 4=Anmelden, 5=Fertig
    private const int TotalSteps = 5;

    public OnboardingWindow()
    {
        InitializeComponent();

        // Eigene Client-ID vorab befüllen (nur relevant ohne mitgelieferte Registrierung)
        var existingId = AppState.Config.ClientId;
        if (MicrosoftAccount.IsUsable(existingId))
            TxtClientId.Text = existingId;

        NoRegistrationHint.Visibility = MicrosoftAccount.HasBuiltInId
            ? Visibility.Collapsed : Visibility.Visible;
        AdvancedIdSection.Visibility  = MicrosoftAccount.HasBuiltInId
            ? Visibility.Collapsed : Visibility.Visible;

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
        // Aus der Outlook-Einrichtung geht es zurueck zur Frage, nicht zur Feed-URL
        if (_step == 4 && _showOutlookSetup)
        {
            _showOutlookSetup = false;
            ShowStep(4);
            return;
        }

        if (_step <= 1) return;
        _step--;
        ShowStep(_step);
    }

    /// <summary>Der Benutzer will Outlook jetzt verknuepfen - Anleitung zeigen.</summary>
    private void BtnLinkNow_Click(object sender, RoutedEventArgs e)
    {
        _skipSignIn       = false;
        _showOutlookSetup = true;
        ShowStep(4);
    }

    /// <summary>Der Benutzer will Outlook spaeter verknuepfen - Schritt ueberspringen.</summary>
    private void BtnLinkLater_Click(object sender, RoutedEventArgs e)
    {
        _skipSignIn       = true;
        _showOutlookSetup = false;
        _step++;
        ShowStep(_step);
    }

    /// <summary>Blendet Frage oder Einrichtung ein.</summary>
    private void ShowStep4Sub(bool showSetup)
    {
        Step4Ask.Visibility   = showSetup ? Visibility.Collapsed : Visibility.Visible;
        Step4Setup.Visibility = showSetup ? Visibility.Visible   : Visibility.Collapsed;
    }

    private void BtnSkipSignIn_Click(object sender, RoutedEventArgs e)
    {
        _skipSignIn       = true;
        _showOutlookSetup = false;
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
        ShowStep4Sub(_showOutlookSetup);
        Step5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        SetDot(Dot1, active: step == 1, done: step > 1);
        SetDot(Dot2, active: step == 2, done: step > 2);
        SetDot(Dot3, active: step == 3, done: step > 3);
        SetDot(Dot4, active: step == 4, done: step > 4);
        SetDot(Dot5, active: step == 5, done: false);

        BtnBack.IsEnabled = step > 1;
        BtnNext.Content   = step == TotalSteps ? "Los geht's!" : "Weiter";

        // Auf der Frage-Seite fuehren die beiden Auswahl-Buttons weiter,
        // nicht der Weiter-Button unten.
        BtnNext.Visibility = step == 4 && !_showOutlookSetup
            ? Visibility.Collapsed
            : Visibility.Visible;

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
        RefreshClientIdState();
        SignInSuccess.Visibility  = _signedIn ? Visibility.Visible : Visibility.Collapsed;
        TxtSignInError.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Die zu verwendende App-ID: eine selbst eingetragene sticht die
    /// mitgelieferte. Null wenn beides fehlt.
    /// </summary>
    private string? EffectiveClientId()
    {
        var custom = TxtClientId.Text.Trim();
        if (MicrosoftAccount.IsUsable(custom)) return custom;
        return MicrosoftAccount.HasBuiltInId ? AppConstants.ClientId : null;
    }

    private void TxtClientId_TextChanged(object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshClientIdState();
        UpdateNextButton();
    }

    /// <summary>Hinweistext und Anmelde-Button an den aktuellen Zustand anpassen.</summary>
    private void RefreshClientIdState()
    {
        ClientIdHint.Text = MicrosoftAccount.IsUsable(TxtClientId.Text.Trim())
            ? "Sieht gut aus. Klicke oben auf «Mit Microsoft anmelden»."
            : "Noch leer — folge der Anleitung unten, um die App-ID zu erstellen.";
        BtnSignIn.IsEnabled = EffectiveClientId() is not null && !_signedIn;
    }

    private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
    {
        var clientId = EffectiveClientId();
        if (clientId is null) return;

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
            // Nur eine selbst eingetragene ID persistieren — die mitgelieferte
            // soll bei einem App-Update automatisch mitwandern.
            var custom = TxtClientId.Text.Trim();
            if (MicrosoftAccount.IsUsable(custom))
                AppState.Config.ClientId = custom;

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
                ? "✅  Outlook ist verknüpft\n"
                : "ℹ️  Outlook nicht verknüpft — der Kalender in der App funktioniert trotzdem. "
                  + "Nachholen kannst du es jederzeit unter Einstellungen.\n") +
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

        // Nur eine eigene Client-ID speichern; die mitgelieferte steht im Code
        var clientId = TxtClientId.Text.Trim();
        if (MicrosoftAccount.IsUsable(clientId))
            config.ClientId = clientId;

        config.AutoRefreshFeed      = ChkAutoRefresh.IsChecked != false;
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
            4 => _signedIn || _skipSignIn,  // Ueberspringen ist jederzeit erlaubt
            _ => true
        };
    }

    // -----------------------------------------------------------------------
    // Security-Helpers
    // -----------------------------------------------------------------------

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
