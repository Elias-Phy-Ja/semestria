using System.Windows;
using ModernWpf.Controls;
using SchulnetzSync.UI.Pages;

namespace SchulnetzSync.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Dashboard beim Start auswählen
        NavView.SelectedItem = NavDashboard;
    }

    private void NavView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag as string)
            {
                case "Dashboard": ContentFrame.Navigate(new DashboardPage()); break;
                case "Events":    ContentFrame.Navigate(new EventsPage());    break;
                case "Settings":  ContentFrame.Navigate(new SettingsPage());  break;
                case "About":     ContentFrame.Navigate(new AboutPage());     break;
            }
        }
    }

    /// <summary>Navigiert von aussen auf eine bestimmte Seite (z.B. aus Settings heraus).</summary>
    public void NavigateTo(string tag)
    {
        switch (tag)
        {
            case "Dashboard": NavView.SelectedItem = NavDashboard; break;
            case "Events":    NavView.SelectedItem = NavEvents;    break;
            case "Settings":  NavView.SelectedItem = NavSettings;  break;
        }
    }
}
