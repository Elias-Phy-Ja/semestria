using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SchulnetzSync.Core.Model;

// Resolve WPF/WinForms type ambiguities in this file
using WpfBrushes    = System.Windows.Media.Brushes;
using WpfButton     = System.Windows.Controls.Button;
using WpfTextBlock  = System.Windows.Controls.TextBlock;
using WpfPage       = System.Windows.Controls.Page;
using WpfBorder     = System.Windows.Controls.Border;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHA         = System.Windows.HorizontalAlignment;

namespace SchulnetzSync.UI.Pages;

public partial class EventsPage : WpfPage
{
    private int    _year;
    private int    _month;
    private DateTime? _selectedDate;
    private string _filter = "All"; // All | Pruefung | Termin

    public EventsPage()
    {
        InitializeComponent();
        var today = DateTime.Today;
        _year  = today.Year;
        _month = today.Month;

        Loaded   += (_, _) => { AppState.Changed += OnStateChanged; Refresh(); };
        Unloaded += (_, _) => AppState.Changed -= OnStateChanged;
    }

    private void OnStateChanged() => Dispatcher.Invoke(Refresh);

    // -----------------------------------------------------------------------
    // Haupt-Refresh
    // -----------------------------------------------------------------------

    private void Refresh()
    {
        var events = FilteredEvents();

        bool hasData = AppState.CachedFeedEvents.Count > 0;
        NoDataHint.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        MainLayout.Visibility = hasData ? Visibility.Visible   : Visibility.Collapsed;

        if (!hasData) return;

        BuildCalendar(events);
        ShowSelectedOrUpcoming(events);
    }

    private IReadOnlyList<SchulnetzEvent> FilteredEvents()
    {
        var all = AppState.CachedFeedEvents;
        return _filter switch
        {
            "Pruefung" => all.Where(e => e.Type == SchulnetzEventType.Pruefung).ToList(),
            "Termin"   => all.Where(e => e.Type == SchulnetzEventType.Termin).ToList(),
            _          => all
        };
    }

    // -----------------------------------------------------------------------
    // Kalender aufbauen
    // -----------------------------------------------------------------------

    private void BuildCalendar(IReadOnlyList<SchulnetzEvent> events)
    {
        TxtMonthYear.Text = new DateTime(_year, _month, 1)
            .ToString("MMMM yyyy",
                System.Globalization.CultureInfo.GetCultureInfo("de-CH"));

        // Alte Tag-Buttons entfernen
        var toRemove = CalendarGrid.Children.OfType<WpfButton>().ToList();
        foreach (var b in toRemove) CalendarGrid.Children.Remove(b);

        var firstDay    = new DateTime(_year, _month, 1);
        int daysInMonth = DateTime.DaysInMonth(_year, _month);
        int todayDay    = (DateTime.Today.Year == _year && DateTime.Today.Month == _month)
                          ? DateTime.Today.Day : -1;

        // Mo=0 … So=6
        int startCol = ((int)firstDay.DayOfWeek + 6) % 7;

        for (int d = 1; d <= daysInMonth; d++)
        {
            var date      = new DateTime(_year, _month, d);
            var dayEvents = events.Where(e => e.Start.Date == date).ToList();
            bool isToday  = d == todayDay;
            bool isSel    = _selectedDate.HasValue && _selectedDate.Value == date;

            int pos = startCol + d - 1;
            int row = pos / 7;
            int col = pos % 7;

            var btn = MakeDayButton(d, dayEvents, isToday, isSel);
            btn.Tag    = date;
            btn.Click += DayButton_Click;
            Grid.SetRow(btn, row);
            Grid.SetColumn(btn, col);
            CalendarGrid.Children.Add(btn);
        }
    }

    private WpfButton MakeDayButton(
        int day,
        List<SchulnetzEvent> events,
        bool isToday,
        bool isSelected)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = WpfHA.Center,
            Width = 30
        };

        // Tages-Zahl in Kreis
        var numBorder = new WpfBorder
        {
            Width       = 28,
            Height      = 28,
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = WpfHA.Center
        };

        if (isSelected)
            numBorder.Background = new SolidColorBrush(Color.FromRgb(0x5C, 0x6E, 0xF7));
        else if (isToday)
            numBorder.Background = new SolidColorBrush(Color.FromArgb(60, 0x5C, 0x6E, 0xF7));
        else
            numBorder.Background = WpfBrushes.Transparent;

        numBorder.Child = new WpfTextBlock
        {
            Text                = day.ToString(),
            FontSize            = 12,
            FontWeight          = isToday || isSelected ? FontWeights.Bold : FontWeights.Normal,
            HorizontalAlignment = WpfHA.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Foreground          = isSelected ? WpfBrushes.White : null
        };
        panel.Children.Add(numBorder);

        // Event-Dots
        if (events.Count > 0)
        {
            var dots = new StackPanel
            {
                Orientation         = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHA.Center,
                Margin              = new Thickness(0, 2, 0, 0)
            };
            bool hasPruefung = events.Any(e => e.Type == SchulnetzEventType.Pruefung);
            bool hasTermin   = events.Any(e => e.Type == SchulnetzEventType.Termin);

            if (hasPruefung) dots.Children.Add(MakeDot("#EF4444"));
            if (hasTermin)   dots.Children.Add(MakeDot("#3B82F6", hasPruefung ? 3 : 0));

            panel.Children.Add(dots);
        }

        return new WpfButton
        {
            Content = panel,
            Style   = (Style)FindResource("DayButton"),
            Height  = 34,
            ToolTip = events.Count > 0
                ? string.Join("\n", events.Select(e =>
                    $"{e.Start.LocalDateTime:HH:mm}  {e.Summary}"))
                : null
        };
    }

    private static Ellipse MakeDot(string hex, double leftMargin = 0)
    {
        var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new Ellipse
        {
            Width             = 5,
            Height            = 5,
            Fill              = new SolidColorBrush(c),
            Margin            = new Thickness(leftMargin, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    // -----------------------------------------------------------------------
    // Rechte Spalte
    // -----------------------------------------------------------------------

    private void ShowSelectedOrUpcoming(IReadOnlyList<SchulnetzEvent> events)
    {
        EventsPanel.Children.Clear();

        if (_selectedDate.HasValue)
        {
            var dayEvents = events
                .Where(e => e.Start.Date == _selectedDate.Value)
                .OrderBy(e => e.Start)
                .ToList();

            TxtSelectedDay.Text = _selectedDate.Value
                .ToString("dddd, d. MMMM yyyy",
                    System.Globalization.CultureInfo.GetCultureInfo("de-CH"));
            TxtSelectedDaySub.Text = dayEvents.Count == 0
                ? "Keine Einträge an diesem Tag."
                : $"{dayEvents.Count} Eintr{(dayEvents.Count == 1 ? "ag" : "äge")}";

            if (dayEvents.Count == 0)
                EventsPanel.Children.Add(MakeEmptyHint("Keine Einträge an diesem Tag."));
            else
                foreach (var ev in dayEvents)
                    EventsPanel.Children.Add(MakeEventCard(ev));
        }
        else
        {
            var upcoming = events
                .Where(e => e.Start.Date >= DateTime.Today)
                .OrderBy(e => e.Start)
                .Take(20)
                .ToList();

            TxtSelectedDay.Text    = "Nächste Einträge";
            TxtSelectedDaySub.Text = "Klicke auf einen Tag im Kalender für Details.";

            if (upcoming.Count == 0)
            {
                EventsPanel.Children.Add(MakeEmptyHint("Keine kommenden Einträge gefunden."));
            }
            else
            {
                string? lastDate = null;
                foreach (var ev in upcoming)
                {
                    var dateStr = ev.Start.LocalDateTime.Date
                        .ToString("dddd, d. MMMM",
                            System.Globalization.CultureInfo.GetCultureInfo("de-CH"));
                    if (dateStr != lastDate)
                    {
                        EventsPanel.Children.Add(MakeDateHeader(dateStr));
                        lastDate = dateStr;
                    }
                    EventsPanel.Children.Add(MakeEventCard(ev));
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Event-Karte
    // -----------------------------------------------------------------------

    private static UIElement MakeEventCard(SchulnetzEvent ev)
    {
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
        var accentColor = isPruefung
            ? Color.FromRgb(0xEF, 0x44, 0x44)
            : Color.FromRgb(0x3B, 0x82, 0xF6);

        var timeStr = ev.IsAllDay
            ? "Ganztägig"
            : $"{ev.Start.LocalDateTime:HH:mm} – {ev.End.LocalDateTime:HH:mm}";

        var titleRow = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 4)
        };
        titleRow.Children.Add(new WpfBorder
        {
            Background      = new SolidColorBrush(accentColor),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(6, 2, 6, 2),
            Margin          = new Thickness(0, 0, 8, 0),
            Child           = new WpfTextBlock
            {
                Text       = isPruefung ? "Prüfung" : "Termin",
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = WpfBrushes.White
            }
        });
        titleRow.Children.Add(new WpfTextBlock
        {
            Text              = ev.Summary,
            FontSize          = 14,
            FontWeight        = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis
        });

        var inner = new StackPanel();
        inner.Children.Add(titleRow);
        inner.Children.Add(new WpfTextBlock
        {
            Text     = timeStr,
            FontSize = 12,
            Opacity  = 0.65,
            Margin   = new Thickness(0, 0, 0, ev.Location != null ? 3 : 0)
        });
        if (!string.IsNullOrWhiteSpace(ev.Location))
        {
            inner.Children.Add(new WpfTextBlock
            {
                Text     = "📍  " + ev.Location,
                FontSize = 12,
                Opacity  = 0.65
            });
        }

        return new WpfBorder
        {
            Background      = (Brush)Application.Current.FindResource(
                                  "SystemControlBackgroundChromeMediumLowBrush"),
            CornerRadius    = new CornerRadius(0, 10, 10, 0),
            BorderBrush     = new SolidColorBrush(accentColor),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding         = new Thickness(14, 12, 14, 12),
            Margin          = new Thickness(0, 0, 0, 10),
            Child           = inner
        };
    }

    private static UIElement MakeDateHeader(string text)
        => new WpfTextBlock
        {
            Text       = text,
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity    = 0.5,
            Margin     = new Thickness(0, 10, 0, 6)
        };

    private static UIElement MakeEmptyHint(string text)
        => new WpfTextBlock
        {
            Text                = text,
            FontSize            = 13,
            Opacity             = 0.45,
            Margin              = new Thickness(0, 20, 0, 0),
            HorizontalAlignment = WpfHA.Center
        };

    // -----------------------------------------------------------------------
    // Ereignis-Handler
    // -----------------------------------------------------------------------

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is DateTime date)
        {
            _selectedDate = (_selectedDate == date) ? null : date;
            Refresh();
        }
    }

    private void BtnPrevMonth_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(-1);
        _year = d.Year; _month = d.Month;
        _selectedDate = null;
        Refresh();
    }

    private void BtnNextMonth_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(1);
        _year = d.Year; _month = d.Month;
        _selectedDate = null;
        Refresh();
    }

    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn) return;
        _filter       = btn.Tag as string ?? "All";
        _selectedDate = null;

        BtnFilterAll.Style      = (Style)FindResource(_filter == "All"      ? "PrimaryButton" : "SecondaryButton");
        BtnFilterPruefung.Style = (Style)FindResource(_filter == "Pruefung" ? "PrimaryButton" : "SecondaryButton");
        BtnFilterTermin.Style   = (Style)FindResource(_filter == "Termin"   ? "PrimaryButton" : "SecondaryButton");

        Refresh();
    }
}
