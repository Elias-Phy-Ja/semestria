using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchulnetzSync.Core.Profiles;

using Brushes        = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily     = System.Windows.Media.FontFamily;
using WpfBorder    = System.Windows.Controls.Border;
using WpfButton    = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox   = System.Windows.Controls.TextBox;

namespace SchulnetzSync.UI;

/// <summary>
/// The profile switcher at the top of the sidebar: who is running, the other profiles one
/// click away, and adding, renaming and removing. Every switch restarts the app in the
/// chosen profile (see <see cref="ProfileSession"/>).
/// </summary>
public partial class MainWindow
{
    /// <summary>"List", "Add" or "Rename": what the open menu shows.</summary>
    private string _profileMenuMode = "List";

    private void ShowProfileHeader()
    {
        var p = ProfileSession.Active;
        TxtProfileName.Text     = p.Name;
        TxtAvatar.Text          = p.Initials;
        AvatarCircle.Background = BrushFrom(p.ColorHex);
        TxtProfileHint.Text     = ProfileSession.Registry.Profiles.Count > 1 ? "Profil wechseln" : "Profil";
    }

    private void BtnProfile_Click(object sender, RoutedEventArgs e)
    {
        _profileMenuMode = "List";
        BuildProfileMenu();
        ProfilePopup.IsOpen = true;
    }

    private void BuildProfileMenu()
    {
        ProfileMenu.Children.Clear();
        var registry = ProfileSession.Registry;

        ProfileMenu.Children.Add(new WpfTextBlock
        {
            Text = "PROFILE", FontSize = 11, FontWeight = FontWeights.Bold, Opacity = 0.55,
            Margin = new Thickness(8, 4, 0, 6)
        });

        foreach (var p in registry.Profiles)
            ProfileMenu.Children.Add(ProfileRow(p, p.Id == registry.ActiveId));

        ProfileMenu.Children.Add(new WpfBorder
        {
            Height = 1, Margin = new Thickness(4, 6, 4, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80))
        });

        switch (_profileMenuMode)
        {
            case "Add":
                ProfileMenu.Children.Add(NameForm(
                    "Name des neuen Profils", "",
                    "Erstellen und wechseln",
                    "Semestria startet neu und richtet das Profil ein, wie bei der ersten Installation.",
                    name => ProfileSession.AddAndSwitch(name)));
                break;

            case "Rename":
                ProfileMenu.Children.Add(NameForm(
                    "Neuer Name", ProfileSession.Active.Name, "Speichern", null,
                    name =>
                    {
                        ProfileSession.Rename(ProfileSession.Active.Id, name);
                        ShowProfileHeader();
                        _profileMenuMode = "List";
                        BuildProfileMenu();
                    }));
                break;

            default:
                ProfileMenu.Children.Add(MenuButton("", "Profil hinzufügen", () =>
                {
                    _profileMenuMode = "Add";
                    BuildProfileMenu();
                }));
                ProfileMenu.Children.Add(MenuButton("", "Umbenennen", () =>
                {
                    _profileMenuMode = "Rename";
                    BuildProfileMenu();
                }));
                if (registry.Profiles.Count > 1)
                    ProfileMenu.Children.Add(MenuButton("", "Dieses Profil entfernen", RemoveActiveProfile));
                break;
        }
    }

    private UIElement ProfileRow(Profile p, bool isActive)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new WpfBorder
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = BrushFrom(p.ColorHex),
            Child = new WpfTextBlock
            {
                Text = p.Initials, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        var name = new WpfTextBlock
        {
            Text = p.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(10, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        if (isActive)
        {
            var tick = new WpfTextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("AccentBrush")
            };
            Grid.SetColumn(tick, 2);
            grid.Children.Add(tick);
        }

        var btn = new WpfButton
        {
            Content = grid,
            Style = (Style)FindResource("FlatMenuButton"),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            ToolTip = isActive ? "Aktuelles Profil" : $"Zu {p.Name} wechseln"
        };
        System.Windows.Automation.AutomationProperties.SetName(btn, isActive ? $"{p.Name} (aktiv)" : $"Zu {p.Name} wechseln");
        btn.Click += (_, _) =>
        {
            if (isActive) { ProfilePopup.IsOpen = false; return; }
            if (AppState.IsSyncing)
            {
                MessageBox.Show("Gerade läuft eine Synchronisation. Wechsle das Profil, sobald sie fertig ist.",
                    "Bitte kurz warten", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ProfilePopup.IsOpen = false;
            ProfileSession.SwitchTo(p.Id);
        };
        return btn;
    }

    private WpfButton MenuButton(string glyph, string label, Action onClick)
    {
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(new WpfTextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 13, Width = 26, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new WpfTextBlock { Text = label, FontSize = 13, Margin = new Thickness(10, 0, 0, 0) });

        var btn = new WpfButton { Content = row, Style = (Style)FindResource("FlatMenuButton") };
        System.Windows.Automation.AutomationProperties.SetName(btn, label);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>A name box with a confirm and a back button, used for adding and renaming.</summary>
    private UIElement NameForm(string label, string initial, string confirm, string? hint, Action<string> onConfirm)
    {
        var panel = new StackPanel { Margin = new Thickness(6, 0, 6, 4) };
        panel.Children.Add(new WpfTextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.6,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var box = new WpfTextBox { Text = initial, Height = 32, FontSize = 13, MaxLength = Profile.MaxNameLength };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        panel.Children.Add(box);

        if (hint is not null)
            panel.Children.Add(new WpfTextBlock
            {
                Text = hint, FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var ok = new WpfButton
        {
            Content = confirm, Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0)
        };
        void Confirm()
        {
            if (string.IsNullOrWhiteSpace(box.Text)) { box.Focus(); return; }
            onConfirm(box.Text);
        }
        ok.Click += (_, _) => Confirm();
        var back = new WpfButton { Content = "Zurück", Style = (Style)FindResource("SecondaryButton") };
        back.Click += (_, _) => { _profileMenuMode = "List"; BuildProfileMenu(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(back);
        panel.Children.Add(buttons);

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)  Confirm();
            if (e.Key == Key.Escape) { _profileMenuMode = "List"; BuildProfileMenu(); }
        };
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return panel;
    }

    private void RemoveActiveProfile()
    {
        var p = ProfileSession.Active;
        ProfilePopup.IsOpen = false;

        var answer = MessageBox.Show(
            $"Profil «{p.Name}» entfernen?\n\n" +
            "Alle Daten dieses Profils auf diesem PC werden gelöscht: Feed-URL, Microsoft-Anmeldung, " +
            "Einstellungen, Aufgaben, Noten. Bereits nach Outlook geschriebene Termine bleiben dort; " +
            "wer sie loswerden will, nutzt vorher in den Einstellungen «Kalender leeren».\n\n" +
            "Semestria startet danach im vorherigen Profil.",
            "Profil entfernen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        ProfileSession.Remove(p.Id);
    }

    private static Brush BrushFrom(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { return Brushes.SlateBlue; }
    }
}
