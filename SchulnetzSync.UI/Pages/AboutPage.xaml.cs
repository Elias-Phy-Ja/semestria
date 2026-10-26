using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SchulnetzSync.UI.Pages;

/// <summary>
/// About page: version, links, the legal texts and the reset button.
/// </summary>
public partial class AboutPage : Page
{
    public AboutPage() => InitializeComponent();

    private void BtnGitHub_Click(object sender, RoutedEventArgs e)
        => OpenUrl(AppConstants.GitHubUrl);

    private void BtnBug_Click(object sender, RoutedEventArgs e)
        => OpenUrl(AppConstants.GitHubUrl + "/issues/new");

    private void BtnAgb_Click(object sender, RoutedEventArgs e)
        => ShowLegal("Nutzungsbedingungen (AGB)", LegalTexts.Agb);

    private void BtnDatenschutz_Click(object sender, RoutedEventArgs e)
        => ShowLegal("Datenschutzerklärung", LegalTexts.Datenschutz);

    private void BtnCloseLegal_Click(object sender, RoutedEventArgs e)
        => LegalViewer.Visibility = Visibility.Collapsed;

    private void ShowLegal(string title, string text)
    {
        TxtLegalTitle.Text     = title;
        TxtLegalContent.Text   = text;
        LegalViewer.Visibility = Visibility.Visible;
    }

    // ── Reset the app ───────────────────────────────────────────────────

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Alle Daten des Profils «{ProfileSession.Active.Name}» (Feed-URL, Microsoft-Konto, " +
            "Einstellungen, Aufgaben, Noten) werden gelöscht und das Onboarding startet neu. " +
            "Andere Profile bleiben unberührt.\n\nFortfahren?",
            "App zurücksetzen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            // Only this profile's folder; the profile list and the other people stay.
            var dir = SchulnetzSync.Core.Profiles.DataPaths.ProfileDirectory;

            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            // Restart so the app comes up clean and runs the onboarding again.
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Zurücksetzen fehlgeschlagen:\n" + ex.Message,
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser available; not worth an error dialog */ }
    }
}
