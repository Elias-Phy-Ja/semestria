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

namespace SchulnetzSync.UI.Pages;

public partial class SettingsPage : Page
{
    private readonly Dictionary<string, string> _calendarMap = new();
    private bool _loadingUi; // Verhindert Theme_Changed während Initialisierung

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            LoadUi();
            await CheckSignInAsync();
        };
    }

    // -----------------------------------------------------------------------
    // Initialisierung
    // -----------------------------------------------------------------------

    private void LoadUi()
    {
        _loadingUi = true;
        try
        {
            var config = AppState.Config;
            // Feed-URL: NIEMALS Token anzeigen — nur Pfad
            var raw = ConfigManager.GetFeedUrl(config);
            TxtFeedUrl.Text = raw ?? "";

            TxtClientId.Text = config.ClientId ?? AppConstants.ClientId;
            if (TxtClientId.Text == "YOUR-CLIENT-ID-HERE") TxtClientId.Text = "";

            ChkPruefungen.IsChecked = config.EnabledTypes.Contains(SchulnetzEventType.Pruefung);
            ChkTermine.IsChecked    = config.EnabledTypes.Contains(SchulnetzEventType.Termin);
            ChkCancel.IsChecked     = config.CancelInsteadOfDelete;
            ChkEnrich.IsChecked     = config.EnrichExamLocationFromLesson;

            // Theme-RadioButton setzen
            switch (config.ThemePreference)
            {
                case "Light": RbLight.IsChecked  = true; break;
                case "Dark":  RbDark.IsChecked   = true; break;
                default:      RbSystem.IsChecked = true; break;
            }
        }
        finally { _loadingUi = false; }
    }

    // -----------------------------------------------------------------------
    // Microsoft-Anmeldung
    // -----------------------------------------------------------------------

    private async Task CheckSignInAsync()
    {
        var clientId = TxtClientId.Text.Trim();
        if (!IsValidClientId(clientId))
        {
            SetAccountState(false, "Nicht angemeldet",
                "Client-ID eintragen um dich anzumelden.");
            return;
        }
        try
        {
            var auth = new MsalAuthProvider(clientId);
            bool ok  = await auth.IsSignedInAsync();
            SetAccountState(ok,
                ok ? "Angemeldet" : "Nicht angemeldet",
                ok ? "SchulnetzSync darf deinen Outlook-Kalender lesen und schreiben."
                   : "Klicke «Anmelden» um den Zugriff zu erlauben.");
            BtnSignOut.IsEnabled = ok;
            BtnSignIn.IsEnabled  = !ok;
            if (ok) await LoadCalendarsAsync(clientId);
        }
        catch
        {
            SetAccountState(false, "Status unbekannt", "Anmeldestatus konnte nicht geprüft werden.");
        }
    }

    // -----------------------------------------------------------------------
    // Theme-Umschalter
    // -----------------------------------------------------------------------

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return; // Keine Aktion während Initialisierung
        if (sender is not WpfRadioButton rb) return;

        var pref = rb.Tag as string;
        ThemeManager.Current.ApplicationTheme = pref switch
        {
            "Light" => (ApplicationTheme?)ApplicationTheme.Light,
            "Dark"  => (ApplicationTheme?)ApplicationTheme.Dark,
            _       => null
        };

        AppState.Config.ThemePreference = (pref == "System") ? null : pref;
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
        var clientId = TxtClientId.Text.Trim();
        if (!IsValidClientId(clientId))
        {
            TxtAuthError.Text       = "Bitte zuerst eine gültige Client-ID eingeben.";
            TxtAuthError.Visibility = Visibility.Visible;
            return;
        }

        BtnSignIn.IsEnabled       = false;
        TxtAuthError.Visibility   = Visibility.Collapsed;
        SetAccountState(false, "Anmeldung läuft…", "Browser öffnet sich…");

        try
        {
            // Frische Instanz — kein Retry-Block durch alten Zustand
            var auth = new MsalAuthProvider(clientId);
            await auth.AcquireTokenInteractiveAsync();

            AppState.Config.ClientId = clientId;
            SetAccountState(true, "Angemeldet",
                "SchulnetzSync darf deinen Outlook-Kalender lesen und schreiben.");
            BtnSignOut.IsEnabled = true;
            await LoadCalendarsAsync(clientId);
        }
        catch (Exception ex)
        {
            var safeMsg = SanitizeError(ex.Message, clientId);
            TxtAuthError.Text       = safeMsg;
            TxtAuthError.Visibility = Visibility.Visible;
            SetAccountState(false, "Anmeldung fehlgeschlagen", "Klicke erneut um es zu versuchen.");
            // Button zurücksetzen → Retry möglich
            BtnSignIn.IsEnabled = true;
        }
    }

    private async void BtnSignOut_Click(object sender, RoutedEventArgs e)
    {
        var clientId = AppState.Config.ClientId ?? TxtClientId.Text.Trim();
        if (!IsValidClientId(clientId)) return;

        try
        {
            var auth = new MsalAuthProvider(clientId);
            await auth.SignOutAsync();
        }
        catch { /* Abmeldung best-effort */ }

        SetAccountState(false, "Abgemeldet", "Du kannst dich jederzeit neu anmelden.");
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

    // -----------------------------------------------------------------------
    // Kalender-Liste
    // -----------------------------------------------------------------------

    private async Task LoadCalendarsAsync(string clientId)
    {
        try
        {
            var auth = new MsalAuthProvider(clientId);
            string token;
            try   { token = await auth.AcquireTokenSilentAsync(); }
            catch { return; } // Kein token → keine Kalender laden

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
            // Nicht-kritisch — Kalender kann leer gelassen werden
        }
    }

    private void CmbCalendar_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    // -----------------------------------------------------------------------
    // Speichern
    // -----------------------------------------------------------------------

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var config  = AppState.Config;
        var feedUrl = TxtFeedUrl.Text.Trim();
        var clientId = TxtClientId.Text.Trim();

        // Feed-URL speichern
        if (!string.IsNullOrEmpty(feedUrl))
            ConfigManager.SetFeedUrl(config, feedUrl);

        // Client-ID speichern (nur wenn gültig)
        if (IsValidClientId(clientId))
            config.ClientId = clientId;

        // Typen
        config.EnabledTypes.Clear();
        if (ChkPruefungen.IsChecked == true) config.EnabledTypes.Add(SchulnetzEventType.Pruefung);
        if (ChkTermine.IsChecked    == true) config.EnabledTypes.Add(SchulnetzEventType.Termin);

        // Optionen
        config.CancelInsteadOfDelete        = ChkCancel.IsChecked == true;
        config.EnrichExamLocationFromLesson = ChkEnrich.IsChecked == true;

        // Kalender
        if (CmbCalendar.SelectedIndex > 0 && CmbCalendar.SelectedItem is string calName)
            config.CalendarId = _calendarMap.FirstOrDefault(kv =>
                kv.Value == calName && kv.Key != "").Key ?? null;
        else
            config.CalendarId = null;

        ConfigManager.Save(config);
        AppState.Notify();

        // Grünes Feedback — automatisch nach 3 s ausblenden
        TxtSaveStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        TxtSaveStatus.Text       = "✅  Einstellungen gespeichert";
        _ = Task.Delay(3000).ContinueWith(
            _ => Dispatcher.Invoke(() => TxtSaveStatus.Text = ""),
            System.Threading.Tasks.TaskScheduler.Default);
    }

    // -----------------------------------------------------------------------
    // Security-Helpers
    // -----------------------------------------------------------------------

    private static bool IsValidClientId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Contains("YOUR", StringComparison.OrdinalIgnoreCase)) return false;
        return id.Length >= 32 && id.Contains('-');
    }

    private static string SanitizeError(string message, string? clientId)
    {
        if (!string.IsNullOrEmpty(clientId))
            message = message.Replace(clientId, "[App-ID]", StringComparison.OrdinalIgnoreCase);

        // URLs entfernen
        message = Regex.Replace(message, @"https?://\S+", "[URL]");

        // Bekannte AADSTS-Codes übersetzen
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
