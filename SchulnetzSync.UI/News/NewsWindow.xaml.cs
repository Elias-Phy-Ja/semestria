using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SchulnetzSync.Core.News;

using WpfBorder    = System.Windows.Controls.Border;
using WpfButton    = System.Windows.Controls.Button;
using WpfHA        = System.Windows.HorizontalAlignment;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace SchulnetzSync.UI.News;

/// <summary>
/// Shows the new developer messages. Whatever was on screen counts as seen once the window
/// closes, however it was closed, so the same message never pops up twice.
/// </summary>
public partial class NewsWindow : Window
{
    private readonly IReadOnlyList<NewsItem> _items;

    public NewsWindow(IReadOnlyList<NewsItem> items)
    {
        InitializeComponent();
        _items = items;

        foreach (var item in items)
            Items.Children.Add(Card(item));

        Closed += (_, _) => AppState.MarkNewsSeen(_items.Select(i => i.Id));
    }

    private UIElement Card(NewsItem item)
    {
        var panel = new StackPanel();

        var glyph = item.Kind switch
        {
            "survey" => "📝",
            "update" => "⬆️",
            _        => "📣"
        };
        panel.Children.Add(new WpfTextBlock
        {
            Text = $"{glyph}  {item.Title}", FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = item.PublishedAt.LocalDateTime.ToString("d. MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-CH")),
            FontSize = 11, Opacity = 0.55, Margin = new Thickness(0, 2, 0, 8)
        });
        if (item.Body.Length > 0)
            panel.Children.Add(new WpfTextBlock
            {
                Text = item.Body, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19
            });

        // Only https links get a button; anything else in the file is ignored on purpose
        if (item.HasSafeLink)
        {
            var label = string.IsNullOrWhiteSpace(item.LinkLabel)
                ? item.Kind == "survey" ? "Zur Umfrage" : "Mehr erfahren"
                : item.LinkLabel;
            var btn = new WpfButton
            {
                Content = label + "  ↗",
                Style = (Style)FindResource("SecondaryButton"),
                HorizontalAlignment = WpfHA.Left,
                Margin = new Thickness(0, 12, 0, 0),
                ToolTip = new Uri(item.Link!).Host
            };
            btn.Click += (_, _) =>
                Process.Start(new ProcessStartInfo(item.Link!) { UseShellExecute = true });
            panel.Children.Add(btn);
        }

        return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
