using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using WpfBorder    = System.Windows.Controls.Border;
using WpfButton    = System.Windows.Controls.Button;
using WpfPage      = System.Windows.Controls.Page;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace SchulnetzSync.UI.Pages;

/// <summary>
/// Every short tour of the app in one list. A click opens the area and runs its tour,
/// whether it was seen before or not.
/// </summary>
public partial class HelpPage : WpfPage
{
    private static readonly Color Accent = Color.FromRgb(0x5C, 0x6E, 0xF7);

    /// <summary>Navigation tag, icon (Segoe MDL2), name and what the tour covers.</summary>
    private static readonly (string Tag, string Glyph, string Title, string Text)[] Tours =
    [
        ("Dashboard", "", "Überblick und Dashboard", "Der Rundgang vom ersten Start: alle Bereiche kurz erklärt."),
        ("Events",    "", "Prüfungen & Termine",     "Woche und Monat, Filter, eigene Einträge, Farben."),
        ("Tasks",     "", "Aufgaben",                "Listen, schnell erfassen, Erinnerungen, abhaken."),
        ("Grades",    "", "Noten",                   "Semester und Jahre, Regel deiner Schule, Zielrechner, Abschluss."),
        ("Settings",  "", "Einstellungen",           "Schulnetz-Link, Outlook, Synchronisation, Darstellung."),
        ("About",     "", "Über & Rechtliches",      "Bedingungen nachlesen, Profil zurücksetzen."),
    ];

    public HelpPage()
    {
        InitializeComponent();
        foreach (var (tag, glyph, title, text) in Tours)
            TourList.Children.Add(Row(tag, glyph, title, text));
    }

    private UIElement Row(string tag, string glyph, string title, string text)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new WpfBorder
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(36, Accent.R, Accent.G, Accent.B)),
            Child = new WpfTextBlock
            {
                Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 18, Foreground = new SolidColorBrush(Accent),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        });

        var texts = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new WpfTextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
        texts.Children.Add(new WpfTextBlock { Text = text, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var go = new WpfTextBlock
        {
            Text = "Anzeigen  ›", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Accent), VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(go, 2);
        grid.Children.Add(go);

        var row = new WpfButton { Content = grid, Style = (Style)FindResource("HelpRow"), Margin = new Thickness(0, 2, 0, 2) };
        System.Windows.Automation.AutomationProperties.SetName(row, $"Anleitung {title}");
        row.Click += (_, _) => MainWindow.Current?.ShowTourOf(tag);
        return row;
    }
}
