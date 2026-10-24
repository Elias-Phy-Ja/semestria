using System.Windows;
using SchulnetzSync.Core.Configuration;

namespace SchulnetzSync.UI.Legal;

/// <summary>
/// Asks for consent again after the legal texts changed. Shown at start-up, before the main
/// window, the update check or any network access, once after an update that
/// raised <see cref="AppConstants.LegalVersion"/>; there is no way into the app without
/// accepting, only out of it.
/// </summary>
public partial class LegalAcceptWindow : Window
{
    /// <summary>True once the user accepted; false when the window was closed any other way.</summary>
    public bool Accepted { get; private set; }

    public LegalAcceptWindow() => InitializeComponent();

    /// <summary>True when the stored consent is older than the current texts.</summary>
    public static bool IsNeeded(SyncConfig config)
        => config.IsOnboardingComplete && config.AcceptedLegalVersion < AppConstants.LegalVersion;

    private void Check_Changed(object sender, RoutedEventArgs e)
        => BtnAccept.IsEnabled = ChkAgb.IsChecked == true && ChkDatenschutz.IsChecked == true;

    private void BtnAccept_Click(object sender, RoutedEventArgs e)
    {
        var config = AppState.Config;
        config.AcceptedLegalVersion = AppConstants.LegalVersion;
        ConfigManager.Save(config);

        Accepted = true;
        Close();
    }

    private void BtnQuit_Click(object sender, RoutedEventArgs e) => Close();
}
