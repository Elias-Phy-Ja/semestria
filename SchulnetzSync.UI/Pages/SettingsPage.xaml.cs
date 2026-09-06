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

public partial class SettingsPage : Page
{
    private readonly Dictionary<string, string> _calendarMap = new();
    // Startet als true — verhindert Theme_Changed während XAML-Init (IsChecked="True" feuert Checked)
    private bool _loadingUi = true;

    public SettingsPage()
    {
        InitializeComponent(); // Hier feuert RbSystem.Checked — _loadingUi=true blockt es
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

            // Im Feld steht nur eine selbst eingetragene ID — die mitgelieferte
            // Registrierung bleibt unsichtbar, damit niemand daran herumschraubt.
            TxtClientId.Text = MicrosoftAccount.UsesCustomId(config)
                ? config.ClientId!.Trim()
                : "";
            AdvancedIdSection.IsExpanded = !MicrosoftAccount.HasBuiltInId;

            ChkPruefungen.IsChecked = config.EnabledTypes.Contains(SchulnetzEventType.Pruefung);
            ChkTermine.IsChecked    = config.EnabledTypes.Contains(SchulnetzEventType.Termin);
            ChkCancel.IsChecked       = config.CancelInsteadOfDelete;
            ChkEnrich.IsChecked       = config.EnrichExamLocationFromLesson;
            ChkAutoRefresh.IsChecked  = config.AutoRefreshFeed;

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
                ok ? "Semestria darf deinen Outlook-Kalender lesen und schreiben — sonst nichts."
                   : "Ein Klick genügt: «Mit Microsoft anmelden». Mehr musst du nicht einrichten.");
            BtnSignOut.IsEnabled = ok;
            BtnSignIn.IsEnabled  = !ok;
            if (ok) await LoadCalendarsAsync(clientId);
        }
        catch
        {
            SetAccountState(false, "Status unbekannt",
                "Konnte nicht geprüft werden — vermutlich keine Internetverbindung.");
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
            // Frische Instanz — kein Retry-Block durch alten Zustand
            var auth = new MsalAuthProvider(clientId);
            await auth.AcquireTokenInteractiveAsync();

            PersistCustomClientId();
            SetAccountState(true, "Outlook ist verknüpft",
                "Semestria darf deinen Outlook-Kalender lesen und schreiben — sonst nichts.");
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
            // Button zurücksetzen → Retry möglich
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

        // Eigene Client-ID speichern; leeres Feld → mitgelieferte Registrierung
        config.ClientId = MicrosoftAccount.IsUsable(clientId) ? clientId : null;

        // Typen
        config.EnabledTypes.Clear();
        if (ChkPruefungen.IsChecked == true) config.EnabledTypes.Add(SchulnetzEventType.Pruefung);
        if (ChkTermine.IsChecked    == true) config.EnabledTypes.Add(SchulnetzEventType.Termin);

        // Optionen
        config.CancelInsteadOfDelete        = ChkCancel.IsChecked == true;
        config.EnrichExamLocationFromLesson = ChkEnrich.IsChecked == true;
        config.AutoRefreshFeed              = ChkAutoRefresh.IsChecked == true;

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

    // -----------------------------------------------------------------------
    // Aufraeumen: alle von Semestria erstellten Outlook-Eintraege loeschen
    // -----------------------------------------------------------------------

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
            "Rückgängig machen lässt sich das nicht — ein erneuter Sync legt die " +
            "Einträge aber wieder an.",
            "Wirklich alle Einträge löschen?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);   // Standard ist "Nein"

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
    /// Die zu verwendende App-ID: eine im Feld eingetragene sticht die
    /// mitgelieferte. Null wenn beides fehlt.
    /// </summary>
    private string? EffectiveClientId()
    {
        var custom = TxtClientId.Text.Trim();
        if (MicrosoftAccount.IsUsable(custom)) return custom;
        return MicrosoftAccount.HasBuiltInId ? AppConstants.ClientId : null;
    }

    /// <summary>Speichert nur eine selbst eingetragene ID in der Konfiguration.</summary>
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
