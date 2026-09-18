using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ModernWpf;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using SchulnetzSync.Core.Calendar;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Model;
using SchulnetzSync.UI.Services;

namespace SchulnetzSync.UI.Pages;

/// <summary>
/// Settings: feed URL, Microsoft account, which types to sync, theme, target calendar,
/// and the button that removes everything the app ever wrote to Outlook.
/// </summary>
public partial class SettingsPage : Page
{
    private readonly Dictionary<string, string> _calendarMap = new();
    // Starts true so Theme_Changed stays quiet during XAML init, where IsChecked="True" fires Checked
    private bool _loadingUi = true;

    public SettingsPage()
    {
        InitializeComponent(); // RbSystem.Checked fires in here; _loadingUi swallows it
        Loaded += async (_, _) =>
        {
            LoadUi();
            await CheckSignInAsync();
        };
    }

    // ── Filling the page from the config ────────────────────────────────

    private void LoadUi()
    {
        _loadingUi = true;
        try
        {
            var config = AppState.Config;
            // Feed URL: show the path only, NEVER the token
            var raw = ConfigManager.GetFeedUrl(config);
            TxtFeedUrl.Text = raw ?? "";

            // The field only ever shows an id the user typed in. The built-in registration
            // stays out of sight so nobody starts fiddling with it.
            TxtClientId.Text = MicrosoftAccount.UsesCustomId(config)
                ? config.ClientId!.Trim()
                : "";
            AdvancedIdSection.IsExpanded = !MicrosoftAccount.HasBuiltInId;

            ChkPruefungen.IsChecked = config.EnabledTypes.Contains(SchulnetzEventType.Pruefung);
            ChkTermine.IsChecked    = config.EnabledTypes.Contains(SchulnetzEventType.Termin);
            ChkCancel.IsChecked       = config.CancelInsteadOfDelete;
            ChkEnrich.IsChecked       = config.EnrichExamLocationFromLesson;
            ChkAutoRefresh.IsChecked  = config.AutoRefreshFeed;

            // Tick the radio button for the stored theme.
            switch (config.ResolveTheme())
            {
                case "Light": RbLight.IsChecked  = true; break;
                case "Dark":  RbDark.IsChecked   = true; break;
                default:      RbSystem.IsChecked = true; break;
            }
        }
        finally { _loadingUi = false; }
    }

    // ── Signing in to Microsoft ─────────────────────────────────────────

    private async Task CheckSignInAsync()
    {
        var clientId = EffectiveClientId();
        if (clientId is null)
        {
            SetAccountState(false, "Outlook nicht verfügbar",
                "Diese Version bringt keine App-Registrierung mit. Trage unter «Erweitert» eine eigene App-ID ein.");
            BtnSignIn.IsEnabled = false;
            return;
        }
        try
        {
            var auth = new MsalAuthProvider(clientId);
            bool ok  = await auth.IsSignedInAsync();
            SetAccountState(ok,
                ok ? "Outlook ist verknüpft" : "Outlook nicht verknüpft",
                ok ? "Semestria darf deinen Outlook-Kalender lesen und schreiben, sonst nichts."
                   : "Ein Klick genügt: «Mit Microsoft anmelden». Mehr musst du nicht einrichten.");
            BtnSignOut.IsEnabled = ok;
            BtnSignIn.IsEnabled  = !ok;
            if (ok) await LoadCalendarsAsync(clientId);
        }
        catch
        {
            SetAccountState(false, "Status unbekannt",
                "Konnte nicht geprüft werden, vermutlich keine Internetverbindung.");
        }
    }

    // ── Theme switch ────────────────────────────────────────────────────

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return; // still filling the page, not a user action
        if (sender is not WpfRadioButton rb) return;

        var pref = rb.Tag as string;
        ThemeManager.Current.ApplicationTheme = pref switch
        {
            "Light" => (ApplicationTheme?)ApplicationTheme.Light,
            "Dark"  => (ApplicationTheme?)ApplicationTheme.Dark,
            _       => null
        };

        // "System" is stored explicitly: null would mean "never chosen" and land on dark
        AppState.Config.ThemePreference = pref;
        ConfigManager.Save(AppState.Config);
    }

    private void BtnOpenAzure_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade")
            { UseShellExecute = true });
        }
        catch { /* Browser nicht verfügbar */ }
    }

    private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
    {
        var clientId = EffectiveClientId();
        if (clientId is null)
        {
            TxtAuthError.Text       = "Keine App-ID verfügbar. Trage unter «Erweitert» eine eigene ein.";
            TxtAuthError.Visibility = Visibility.Visible;
            return;
        }

        BtnSignIn.IsEnabled       = false;
        TxtAuthError.Visibility   = Visibility.Collapsed;
        SetAccountState(false, "Anmeldung läuft…", "Es öffnet sich ein Microsoft-Fenster.");

        try
        {
            // Fresh instance, so an earlier failure cannot block the retry.
            var auth = new MsalAuthProvider(clientId);
            await auth.AcquireTokenInteractiveAsync();

            PersistCustomClientId();
            SetAccountState(true, "Outlook ist verknüpft",
                "Semestria darf deinen Outlook-Kalender lesen und schreiben, sonst nichts.");
            BtnSignOut.IsEnabled = true;
            await LoadCalendarsAsync(clientId);
        }
        catch (Exception ex)
        {
            var safeMsg = SanitizeError(ex.Message, clientId);
            TxtAuthError.Text       = safeMsg;
            TxtAuthError.Visibility = Visibility.Visible;
            SetAccountState(false, "Anmeldung fehlgeschlagen",
                "Prüfe die App-ID und versuche es nochmals.");
            // Put the button back so another attempt is possible.
            BtnSignIn.IsEnabled = true;
        }
    }

    private async void BtnSignOut_Click(object sender, RoutedEventArgs e)
    {
        var clientId = EffectiveClientId();
        if (clientId is null) return;

        try
        {
            var auth = new MsalAuthProvider(clientId);
            await auth.SignOutAsync();
        }
        catch { /* Abmeldung best-effort */ }

        SetAccountState(false, "Outlook nicht verknüpft",
                "Abgemeldet. Du kannst dich jederzeit neu anmelden.");
        BtnSignOut.IsEnabled = false;
        BtnSignIn.IsEnabled  = true;
        CmbCalendar.Items.Clear();
    }

    private void SetAccountState(bool signed, string label, string detail)
    {
        Dispatcher.Invoke(() =>
        {
            TxtAccountStatus.Text = label;
            TxtAccountDetail.Text = detail;
            AccountDot.Fill = new SolidColorBrush(signed
                ? Color.FromRgb(0x22, 0xC5, 0x5E)
                : Color.FromRgb(0x9C, 0xA3, 0xAF));
        });
    }

    // ── The calendar picker ─────────────────────────────────────────────

    private async Task LoadCalendarsAsync(string clientId)
    {
        try
        {
            var auth = new MsalAuthProvider(clientId);
            string token;
            try   { token = await auth.AcquireTokenSilentAsync(); }
            catch { return; } // no token, so there is nothing to list

            var target = new GraphCalendarTarget(token);
            var cals   = await target.GetCalendarsAsync();

            Dispatcher.Invoke(() =>
            {
                CmbCalendar.Items.Clear();
                _calendarMap.Clear();
                CmbCalendar.Items.Add("Primärer Kalender (Standard)");
                _calendarMap[""] = "Primärer Kalender (Standard)";

                foreach (var (id, name) in cals)
                {
                    CmbCalendar.Items.Add(name);
                    _calendarMap[id] = name;
                }

                var currentId = AppState.Config.CalendarId;
                CmbCalendar.SelectedIndex =
                    (currentId is null || !_calendarMap.ContainsKey(currentId))
                    ? 0
                    : CmbCalendar.Items.IndexOf(_calendarMap[currentId]);
            });
        }
        catch
        {
            // Not important enough to complain about: the field may stay empty.
        }
    }

    private void CmbCalendar_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    // ── Saving ──────────────────────────────────────────────────────────

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var config  = AppState.Config;
        var feedUrl = TxtFeedUrl.Text.Trim();
        var clientId = TxtClientId.Text.Trim();

        // Feed URL, encrypted on the way in.
        if (!string.IsNullOrEmpty(feedUrl))
            ConfigManager.SetFeedUrl(config, feedUrl);

        // Custom client id; an empty field falls back to the built-in registration.
        config.ClientId = MicrosoftAccount.IsUsable(clientId) ? clientId : null;

        // Which types go to Outlook.
        config.EnabledTypes.Clear();
        if (ChkPruefungen.IsChecked == true) config.EnabledTypes.Add(SchulnetzEventType.Pruefung);
        if (ChkTermine.IsChecked    == true) config.EnabledTypes.Add(SchulnetzEventType.Termin);

        // The two behaviour switches.
        config.CancelInsteadOfDelete        = ChkCancel.IsChecked == true;
        config.EnrichExamLocationFromLesson = ChkEnrich.IsChecked == true;
        config.AutoRefreshFeed              = ChkAutoRefresh.IsChecked == true;

        // Target calendar.
        if (CmbCalendar.SelectedIndex > 0 && CmbCalendar.SelectedItem is string calName)
            config.CalendarId = _calendarMap.FirstOrDefault(kv =>
                kv.Value == calName && kv.Key != "").Key ?? null;
        else
            config.CalendarId = null;

        ConfigManager.Save(config);
        AppState.Notify();

        // Green confirmation that fades out by itself after 3 s.
        TxtSaveStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        TxtSaveStatus.Text       = "✅  Einstellungen gespeichert";
        _ = Task.Delay(3000).ContinueWith(
            _ => Dispatcher.Invoke(() => TxtSaveStatus.Text = ""),
            System.Threading.Tasks.TaskScheduler.Default);
    }

    // ── Keeping secrets off the screen ──────────────────────────────────

    // ── Clean-up: remove every Outlook entry Semestria ever created ─────

    private async void BtnPurgeAll_Click(object sender, RoutedEventArgs e)
    {
        var clientId = EffectiveClientId();
        if (clientId is null)
        {
            TxtPurgeStatus.Text = "Outlook ist nicht verknüpft.";
            return;
        }

        var confirm = MessageBox.Show(
            "Alle Kalendereinträge löschen, die Semestria in Outlook erstellt hat?\n\n" +
            "Deine eigenen Termine bleiben unberührt.\n" +
            "Rückgängig machen lässt sich das nicht. Ein erneuter Sync legt die " +
            "Einträge aber wieder an.",
            "Wirklich alle Einträge löschen?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);   // default to No, this one cannot be undone

        if (confirm != MessageBoxResult.Yes) return;

        SetPurgeBusy(true);
        TxtPurgeStatus.Text = "Verbinde mit Outlook…";

        try
        {
            var auth = new MsalAuthProvider(clientId);
            string token;
            try   { token = await auth.AcquireTokenSilentAsync(); }
            catch (InteractiveLoginRequiredException)
            { token = await auth.AcquireTokenInteractiveAsync(); }

            var target = new GraphCalendarTarget(token);
            var count  = await target.PurgeAllAsync(
                AppState.Config.CalendarId,
                new Progress<string>(msg => Dispatcher.Invoke(
                    () => TxtPurgeStatus.Text = msg)));

            TxtPurgeStatus.Text = count == 0
                ? "Keine Einträge von Semestria gefunden."
                : $"{count} Einträge gelöscht.";
        }
        catch (Exception ex)
        {
            TxtPurgeStatus.Text = "Fehler: " + SanitizeError(ex.Message, clientId);
        }
        finally
        {
            SetPurgeBusy(false);
        }
    }

    private void SetPurgeBusy(bool busy)
    {
        PurgeRing.IsActive   = busy;
        BtnPurgeAll.IsEnabled = !busy;
    }

    /// <summary>
    /// The app id to use: one typed into the field beats the built-in one.
    /// Null when there is neither.
    /// </summary>
    private string? EffectiveClientId()
    {
        var custom = TxtClientId.Text.Trim();
        if (MicrosoftAccount.IsUsable(custom)) return custom;
        return MicrosoftAccount.HasBuiltInId ? AppConstants.ClientId : null;
    }

    /// <summary>Only ever writes a custom id to the config, never the built-in one.</summary>
    private void PersistCustomClientId()
    {
        var custom = TxtClientId.Text.Trim();
        if (MicrosoftAccount.IsUsable(custom))
            AppState.Config.ClientId = custom;
    }


    private static string SanitizeError(string message, string? clientId)
    {
        if (!string.IsNullOrEmpty(clientId))
            message = message.Replace(clientId, "[App-ID]", StringComparison.OrdinalIgnoreCase);

        // Take out any URL — it could be the feed URL with its token.
        message = Regex.Replace(message, @"https?://\S+", "[URL]");

        // Turn the AADSTS codes we know about into something a person can act on.
        if (message.Contains("AADSTS700016"))
            return "App-ID nicht gefunden. Überprüfe die Client-ID auf portal.azure.com.";
        if (message.Contains("AADSTS65004"))
            return "Zugriff verweigert. Hast du Calendars.ReadWrite in der App-Registrierung gesetzt?";
        if (message.Contains("canceled") || message.Contains("aborted"))
            return "Anmeldung abgebrochen. Du kannst es erneut versuchen.";

        var first = message.Split('\n')[0].Trim();
        return first.Length > 120 ? first[..120] + "…" : first;
    }
}
