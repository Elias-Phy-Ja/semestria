using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SchulnetzSync.UI.Pages;

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

    // -----------------------------------------------------------------------
    // App zurücksetzen
    // -----------------------------------------------------------------------

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Alle Einstellungen (Feed-URL, Konto, Einstellungen) werden gelöscht " +
            "und das Onboarding startet neu.\n\nFortfahren?",
            "App zurücksetzen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Semestria");

            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            // App neu starten
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
        catch { /* Browser nicht verfügbar */ }
    }
}
