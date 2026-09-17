using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SchulnetzSync.Core.Calendar;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.UI.Services;
using Brushes             = System.Windows.Media.Brushes;
using Clipboard           = System.Windows.Clipboard;
using FontFamily          = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation         = System.Windows.Controls.Orientation;

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

    private static readonly string[] StepNames =
        ["Willkommen", "Bedingungen", "Schulnetz verbinden", "Outlook", "Fertig"];

    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

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

        ShowStep(1);
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
        catch { /* Browser nicht verfügbar, kein Problem */ }
    }

    private void ShowStep(int step)
    {
        Step1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        ShowStep4Sub(_showOutlookSetup);
        Step5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        BuildStepRail(step);
        ContentScroll.ScrollToTop();

        BtnBack.IsEnabled   = step > 1;
        TxtStepCounter.Text = $"{step} / {TotalSteps}";
        TxtNextLabel.Text   = step switch
        {
            1          => "Los geht's",
            TotalSteps => "Semestria öffnen",
            _          => "Weiter",
        };

        // Auf der Frage-Seite fuehren die beiden Auswahl-Buttons weiter,
        // nicht der Weiter-Button unten.
        BtnNext.Visibility = step == 4 && !_showOutlookSetup
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (step == 4) OnEnterStep4();
        if (step == 5) BuildFinishSummary();

        UpdateNextButton();
    }

    /// <summary>Schrittleiste links: erledigt mit Haken, aktueller Schritt hervorgehoben.</summary>
    private void BuildStepRail(int current)
    {
        StepRail.Children.Clear();
        var accent = Color.FromRgb(0x5C, 0x6E, 0xF7);

        for (int i = 1; i <= TotalSteps; i++)
        {
            bool done   = i < current;
            bool active = i == current;

            var badge = new Border
            {
                Width           = 28,
                Height          = 28,
                CornerRadius    = new CornerRadius(14),
                Background      = new SolidColorBrush(active ? Colors.White
                                : done ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)
                                : Colors.Transparent),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(active || done ? (byte)0x00 : (byte)0x66, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text                = done ? "\uE73E" : i.ToString(),
                    FontFamily          = done ? new FontFamily(IconFont) : new FontFamily("Segoe UI"),
                    FontSize            = done ? 12 : 12.5,
                    FontWeight          = FontWeights.Bold,
                    Foreground          = new SolidColorBrush(active ? accent : Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                },
            };

            var label = new TextBlock
            {
                Text              = StepNames[i - 1],
                Foreground        = Brushes.White,
                FontSize          = 14,
                FontWeight        = active ? FontWeights.SemiBold : FontWeights.Normal,
                Opacity           = active ? 1.0 : done ? 0.85 : 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(14, 0, 0, 0),
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(badge);
            row.Children.Add(label);
            StepRail.Children.Add(row);

            // Verbindungslinie zum nächsten Schritt
            if (i < TotalSteps)
                StepRail.Children.Add(new Border
                {
                    Width               = 1.5,
                    Height              = 18,
                    Margin              = new Thickness(13.25, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background          = new SolidColorBrush(Color.FromArgb(done ? (byte)0x80 : (byte)0x40, 0xFF, 0xFF, 0xFF)),
                });
        }
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
            ShowFeedProblem("Bitte den Kalender-Link einfügen.");
            return false;
        }
        if (!url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
         && !url.StartsWith("https://",  StringComparison.OrdinalIgnoreCase)
         && !url.StartsWith("http://",   StringComparison.OrdinalIgnoreCase))
        {
            ShowFeedProblem("Der Link muss mit webcal://, https:// oder http:// beginnen.");
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
    // Schritt 2: Rechtliches
    // -----------------------------------------------------------------------

    private void Legal_CheckChanged(object sender, RoutedEventArgs e)
        => UpdateNextButton();

    // -----------------------------------------------------------------------
    // Schritt 3: Feed-URL
    // -----------------------------------------------------------------------

    private void TxtFeedUrl_TextChanged(object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        TxtFeedStatus.Text = "Der Link wird verschlüsselt gespeichert und verlässt dein Gerät nie.";
        TxtFeedStatus.ClearValue(TextBlock.ForegroundProperty);
        TxtFeedStatus.Opacity   = 0.55;
        FeedStatusGlyph.Text    = "\uE72E";
        FeedStatusGlyph.ClearValue(TextBlock.ForegroundProperty);
        FeedStatusGlyph.Opacity = 0.55;
        UpdateNextButton();
    }

    private void ShowFeedProblem(string message)
    {
        var red = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        TxtFeedStatus.Text         = message;
        TxtFeedStatus.Foreground   = red;
        TxtFeedStatus.Opacity      = 1;
        FeedStatusGlyph.Text       = "\uE7BA";
        FeedStatusGlyph.Foreground = red;
        FeedStatusGlyph.Opacity    = 1;
    }

    private void BtnPasteFeed_Click(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText()) return;
        TxtFeedUrl.Text = Clipboard.GetText().Trim();
        TxtFeedUrl.Focus();
        TxtFeedUrl.CaretIndex = TxtFeedUrl.Text.Length;
    }

    // -----------------------------------------------------------------------
    // Schritt 4: Microsoft-Anmeldung
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
            : "Noch leer. Folge der Anleitung unten, um die App-ID zu erstellen.";
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
            // Frische Instanz bei jedem Versuch, kein eingefrorener Zustand
            var auth = new MsalAuthProvider(clientId);
            await auth.AcquireTokenInteractiveAsync();

            _signedIn = true;
            SignInSuccess.Visibility = Visibility.Visible;
            BtnSignIn.Visibility     = Visibility.Collapsed;
            // Nur eine selbst eingetragene ID persistieren; die mitgelieferte
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
    // Schritt 5: Fertig
    // -----------------------------------------------------------------------

    private void BuildFinishSummary()
    {
        SummaryList.Children.Clear();
        AddSummaryRow("\uE71B", "Schulnetz verbunden", SafeDisplayUrl(TxtFeedUrl.Text.Trim()), ok: true);
        AddSummaryRow("\uE8A7",
            _signedIn ? "Outlook verknüpft" : "Outlook nicht verknüpft",
            _signedIn ? "Einträge erscheinen auch in deinem Outlook-Kalender."
                      : "Kein Problem, der Kalender in der App funktioniert. Nachholen geht jederzeit in den Einstellungen.",
            ok: _signedIn);
        AddSummaryRow("\uE8F4", "Bedingungen akzeptiert", "Nutzungsbedingungen und Datenschutzerklärung", ok: true, last: true);
    }

    private void AddSummaryRow(string glyph, string title, string detail, bool ok, bool last = false)
    {
        var tint = ok ? Color.FromRgb(0x22, 0xC5, 0x5E) : Color.FromRgb(0x8A, 0x8F, 0x98);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, last ? 0 : 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new Border
        {
            Width             = 34,
            Height            = 34,
            CornerRadius      = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Top,
            Background        = new SolidColorBrush(Color.FromArgb(0x26, tint.R, tint.G, tint.B)),
            Child = new TextBlock
            {
                Text                = ok ? "\uE73E" : glyph,
                FontFamily          = new FontFamily(IconFont),
                FontSize            = 14,
                Foreground          = new SolidColorBrush(tint),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            },
        });

        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text         = detail,
            FontSize     = 12.5,
            Opacity      = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        SummaryList.Children.Add(grid);
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
    /// Zeigt von einer Feed-URL nur den Host, KEIN Token und keinen Query-String.
    /// Schützt vor unabsichtlichem Anzeigen des persönlichen Tokens.
    /// </summary>
    private static string SafeDisplayUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "(nicht gesetzt)";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host + "  (Token ausgeblendet)";
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
