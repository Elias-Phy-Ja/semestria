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

/// <summary>
/// The first-start wizard: welcome, legal texts, feed URL, Microsoft account, done.
///
/// Only the feed URL is really required. Everything else can be skipped, and the app is
/// fully usable without an Outlook connection.
/// </summary>
public partial class OnboardingWindow : Window
{
    private int  _step       = 1;
    private bool _signedIn   = false;
    private bool _skipSignIn = false;

    /// <summary>
    /// Step 4 comes in two halves: first the question, then — only on a yes — the actual
    /// setup. That setup is by far the most complicated part of the app, and nobody who
    /// does not need it should be made to walk through it.
    /// </summary>
    private bool _showOutlookSetup;

    // Steps: 1 welcome, 2 legal, 3 feed URL, 4 sign-in, 5 done
    private const int TotalSteps = 5;

    private static readonly string[] StepNames =
        ["Willkommen", "Bedingungen", "Schulnetz verbinden", "Outlook", "Fertig"];

    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public OnboardingWindow()
    {
        InitializeComponent();

        // Pre-fill a custom client id; only ever relevant without the built-in registration.
        var existingId = AppState.Config.ClientId;
        if (MicrosoftAccount.IsUsable(existingId))
            TxtClientId.Text = existingId;

        NoRegistrationHint.Visibility = MicrosoftAccount.HasBuiltInId
            ? Visibility.Collapsed : Visibility.Visible;
        AdvancedIdSection.Visibility  = MicrosoftAccount.HasBuiltInId
            ? Visibility.Collapsed : Visibility.Visible;

        ShowStep(1);
    }

    // ── Navigation ──────────────────────────────────────────────────────

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAdvance()) return;
        if (_step == TotalSteps) { Complete(); return; }
        _step++;
        ShowStep(_step);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        // Back out of the Outlook setup lands on the question, not on the feed URL.
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

    /// <summary>They want Outlook now, so show the walkthrough.</summary>
    private void BtnLinkNow_Click(object sender, RoutedEventArgs e)
    {
        _skipSignIn       = false;
        _showOutlookSetup = true;
        ShowStep(4);
    }

    /// <summary>They want Outlook later, so skip the whole step.</summary>
    private void BtnLinkLater_Click(object sender, RoutedEventArgs e)
    {
        _skipSignIn       = true;
        _showOutlookSetup = false;
        _step++;
        ShowStep(_step);
    }

    /// <summary>Switches between the question and the setup half of step 4.</summary>
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

        // On the question page the two choice buttons move things along, not the
        // "Weiter" button at the bottom.
        BtnNext.Visibility = step == 4 && !_showOutlookSetup
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (step == 4) OnEnterStep4();
        if (step == 5) BuildFinishSummary();

        UpdateNextButton();
    }

    /// <summary>The step rail on the left: ticks for done, highlight for the current one.</summary>
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

            // Connecting line down to the next step
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

    // ── Validation per step ─────────────────────────────────────────────

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

    // ── Step 2: the legal texts ─────────────────────────────────────────

    private void Legal_CheckChanged(object sender, RoutedEventArgs e)
        => UpdateNextButton();

    // ── Step 3: the feed URL ────────────────────────────────────────────

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

    // ── Step 4: signing in to Microsoft ─────────────────────────────────

    private void OnEnterStep4()
    {
        RefreshClientIdState();
        SignInSuccess.Visibility  = _signedIn ? Visibility.Visible : Visibility.Collapsed;
        TxtSignInError.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The app id to use: one the user typed in beats the built-in one.
    /// Null when there is neither.
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

    /// <summary>Brings the hint text and the sign-in button in line with the current state.</summary>
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
            // A fresh instance per attempt, so no stale state survives a failed try.
            var auth = new MsalAuthProvider(clientId);
            await auth.AcquireTokenInteractiveAsync();

            _signedIn = true;
            SignInSuccess.Visibility = Visibility.Visible;
            BtnSignIn.Visibility     = Visibility.Collapsed;
            // Only store an id the user typed in. The built-in one has to be free to
            // change with an app update, which it cannot do once it is written to disk.
            var custom = TxtClientId.Text.Trim();
            if (MicrosoftAccount.IsUsable(custom))
                AppState.Config.ClientId = custom;

            UpdateNextButton();
        }
        catch (Exception ex)
        {
            // Error message with the client id and any URL stripped out.
            var safeMsg = SanitizeErrorMessage(ex.Message, clientId);
            TxtSignInError.Text       = "Fehler: " + safeMsg;
            TxtSignInError.Visibility = Visibility.Visible;
            // Re-enable the button so another attempt is possible.
            BtnSignIn.IsEnabled = true;
        }
    }

    // ── Step 5: done ────────────────────────────────────────────────────

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

    // ── Wrapping up ─────────────────────────────────────────────────────

    private void Complete()
    {
        var config = AppState.Config;

        // The feed URL goes to disk encrypted, never in plain text.
        var url = TxtFeedUrl.Text.Trim();
        if (!string.IsNullOrEmpty(url))
            ConfigManager.SetFeedUrl(config, url);

        // Again: only a custom client id is saved, the built-in one lives in the code.
        var clientId = TxtClientId.Text.Trim();
        if (MicrosoftAccount.IsUsable(clientId))
            config.ClientId = clientId;

        config.AutoRefreshFeed      = ChkAutoRefresh.IsChecked != false;
        config.IsOnboardingComplete = true;
        config.AcceptedLegalVersion = AppConstants.LegalVersion;
        ConfigManager.Save(config);
        AppState.Reload();

        // Close up; App.xaml.cs takes over and opens the main window.
        Close();
    }

    // ── When the Weiter button is allowed to be enabled ──────────────────

    private void UpdateNextButton()
    {
        BtnNext.IsEnabled = _step switch
        {
            2 => ChkAgb.IsChecked == true && ChkDatenschutz.IsChecked == true,
            3 => !string.IsNullOrWhiteSpace(TxtFeedUrl.Text),
            4 => _signedIn || _skipSignIn,  // skipping is always allowed
            _ => true
        };
    }

    // ── Keeping the token out of sight ──────────────────────────────────

    /// <summary>
    /// Shows the host of a feed URL and nothing else — no query string, so no token.
    /// The URL ends up on screen in a few places, and this is what makes that safe.
    /// </summary>
    private static string SafeDisplayUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "(nicht gesetzt)";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host + "  (Token ausgeblendet)";
        return url.Length > 40 ? url[..40] + "..." : url;
    }

    /// <summary>
    /// Strips client ids and URLs out of an exception message before it is shown.
    /// Error texts love to quote the request that failed.
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
