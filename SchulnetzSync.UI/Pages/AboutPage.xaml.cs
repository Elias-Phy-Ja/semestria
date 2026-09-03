using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SchulnetzSync.UI.Pages;

public partial class AboutPage : Page
{
    public AboutPage() => InitializeComponent();

    private void BtnGitHub_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(AppConstants.GitHubUrl) { UseShellExecute = true });

    private void BtnBug_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(
            AppConstants.GitHubUrl + "/issues/new") { UseShellExecute = true });

    private void BtnAgb_Click(object sender, RoutedEventArgs e)
        => ShowLegal("Nutzungsbedingungen (AGB)", LegalTexts.Agb);

    private void BtnDatenschutz_Click(object sender, RoutedEventArgs e)
        => ShowLegal("Datenschutzerklärung", LegalTexts.Datenschutz);

    private void BtnCloseLegal_Click(object sender, RoutedEventArgs e)
        => LegalViewer.Visibility = Visibility.Collapsed;

    private void ShowLegal(string title, string text)
    {
        TxtLegalTitle.Text   = title;
        TxtLegalContent.Text = text;
        LegalViewer.Visibility = Visibility.Visible;
    }
}
