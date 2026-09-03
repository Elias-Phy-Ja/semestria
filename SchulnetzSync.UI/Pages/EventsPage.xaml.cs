using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchulnetzSync.Core.Model;

// WPF/WinForms-Typ-Ambiguität auflösen
using WpfBorder      = System.Windows.Controls.Border;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfHA          = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPage        = System.Windows.Controls.Page;
using WpfTextBlock   = System.Windows.Controls.TextBlock;

namespace SchulnetzSync.UI.Pages;

public partial class EventsPage : WpfPage
{
    private int      _year;
    private int      _month;
    private DateTime? _selectedDate;
    private SchulnetzEvent? _selectedEvent;
    private string   _filter = "All";

    private static readonly CultureInfo _deCH =
        CultureInfo.GetCultureInfo("de-CH");

    // Farben aus dem Design
    private static readonly Color _pruefungColor = Color.FromRgb(0xEF, 0x44, 0x44);
    private static readonly Color _terminColor   = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color _accentColor   = Color.FromRgb(0x5C, 0x6E, 0xF7);

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

    // ══════════════════════════════════════════════════════════════════════
    // Haupt-Refresh
    // ══════════════════════════════════════════════════════════════════════

    private void Refresh()
    {
        bool hasData = AppState.CachedFeedEvents.Count > 0;
        NoDataHint.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        MainLayout.Visibility = hasData ? Visibility.Visible   : Visibility.Collapsed;
        if (!hasData) return;

        BuildCalendar(FilteredEvents());
    }

    private IReadOnlyList<SchulnetzEvent> FilteredEvents()
    {
        var suppressed = AppState.SuppressedKeys;
        return (AppState.CachedFeedEvents
            .Where(e => !suppressed.Contains(e.Key)))
            .Where(e => _filter switch
            {
                "Pruefung" => e.Type == SchulnetzEventType.Pruefung,
                "Termin"   => e.Type == SchulnetzEventType.Termin,
                _          => true
            })
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Kalender aufbauen
    // ══════════════════════════════════════════════════════════════════════

    private void BuildCalendar(IReadOnlyList<SchulnetzEvent> events)
    {
        TxtMonthYear.Text = new DateTime(_year, _month, 1)
            .ToString("MMMM yyyy", _deCH);

        CalendarGrid.Children.Clear();

        var firstDay    = new DateTime(_year, _month, 1);
        int daysInMonth = DateTime.DaysInMonth(_year, _month);
        // Mo=0 … So=6
        int startCol    = ((int)firstDay.DayOfWeek + 6) % 7;
        var today       = DateTime.Today;
        var prevFirst   = firstDay.AddMonths(-1);
        int prevDays    = DateTime.DaysInMonth(prevFirst.Year, prevFirst.Month);

        for (int cellIdx = 0; cellIdx < 42; cellIdx++)
        {
            int row = cellIdx / 7;
            int col = cellIdx % 7;

            DateTime date;
            bool isCurMonth;

            if (cellIdx < startCol)
            {
                // Vormonat
                date       = new DateTime(prevFirst.Year, prevFirst.Month,
                                 prevDays - startCol + cellIdx + 1);
                isCurMonth = false;
            }
            else if (cellIdx >= startCol + daysInMonth)
            {
                // Nächster Monat
                date       = firstDay.AddMonths(1)
                                 .AddDays(cellIdx - startCol - daysInMonth);
                isCurMonth = false;
            }
            else
            {
                date       = new DateTime(_year, _month, cellIdx - startCol + 1);
                isCurMonth = true;
            }

            bool isToday  = date == today;
            bool isSel    = _selectedDate.HasValue && date == _selectedDate.Value;
            bool isWeekend = col >= 5;

            var dayEvents = events.Where(e => e.Start.Date == date).ToList();
            var cell = MakeCell(date, dayEvents, isCurMonth, isToday, isSel, isWeekend, row, col);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            CalendarGrid.Children.Add(cell);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Zelle erstellen (Outlook-Stil)
    // ══════════════════════════════════════════════════════════════════════

    private UIElement MakeCell(
        DateTime date,
        List<SchulnetzEvent> dayEvents,
        bool isCurMonth, bool isToday, bool isSelected,
        bool isWeekend,  int row,      int col)
    {
        // Zellen-Hintergrund
        Brush bg;
        if (isToday && isCurMonth)
            bg = new SolidColorBrush(Color.FromArgb(18, _accentColor.R, _accentColor.G, _accentColor.B));
        else if (isSelected)
            bg = new SolidColorBrush(Color.FromArgb(25, _accentColor.R, _accentColor.G, _accentColor.B));
        else if (!isCurMonth || isWeekend)
            bg = new SolidColorBrush(Color.FromArgb(12, 0x80, 0x80, 0x80));
        else
            bg = WpfBrushes.Transparent;

        var lineColor = new SolidColorBrush(Color.FromArgb(40, 0x80, 0x80, 0x80));

        // Linke Begrenzung nur bei erster Spalte; sonst nur rechts + unten
        var borderThickness = new Thickness(
            col == 0 ? 1 : 0,
            0,
            1,
            1);

        var cell = new WpfBorder
        {
            Background      = bg,
            BorderBrush     = lineColor,
            BorderThickness = borderThickness,
            Tag             = date,
            Cursor          = WpfCursors.Hand
        };
        cell.MouseLeftButtonUp += DayCell_MouseUp;

        var cellPanel = new StackPanel
        {
            Margin = new Thickness(4, 3, 4, 3),
            IsHitTestVisible = false  // Klicks gehen durch zum Border
        };

        // Tages-Zahl
        if (isToday && isCurMonth)
        {
            // Heute: Zahl in farbigem Kreis
            var circle = new WpfBorder
            {
                Width               = 24,
                Height              = 24,
                CornerRadius        = new CornerRadius(12),
                Background          = new SolidColorBrush(_accentColor),
                HorizontalAlignment = WpfHA.Right,
                Margin              = new Thickness(0, 0, 2, 2),
                Child               = new WpfTextBlock
                {
                    Text                = date.Day.ToString(),
                    FontSize            = 11,
                    FontWeight          = FontWeights.Bold,
                    Foreground          = WpfBrushes.White,
                    HorizontalAlignment = WpfHA.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            };
            cellPanel.Children.Add(circle);
        }
        else
        {
            cellPanel.Children.Add(new WpfTextBlock
            {
                Text                = date.Day.ToString(),
                FontSize            = 11,
                FontWeight          = isToday ? FontWeights.Bold : FontWeights.Normal,
                Opacity             = isCurMonth ? 0.80 : 0.28,
                HorizontalAlignment = WpfHA.Right,
                Margin              = new Thickness(0, 0, 4, 2)
            });
        }

        // Event-Pills (max. 4, dann "+N weitere")
        const int maxPills = 4;
        for (int i = 0; i < Math.Min(dayEvents.Count, maxPills); i++)
            cellPanel.Children.Add(MakeEventPill(dayEvents[i], isCurMonth));

        if (dayEvents.Count > maxPills)
        {
            var more = new WpfTextBlock
            {
                Text    = $"+{dayEvents.Count - maxPills} weitere",
                FontSize = 9,
                Opacity  = 0.55,
                Margin   = new Thickness(3, 1, 0, 0)
            };
            cellPanel.Children.Add(more);
        }

        cell.Child = cellPanel;
        return cell;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Event-Pill (anklickbar)
    // ══════════════════════════════════════════════════════════════════════

    private UIElement MakeEventPill(SchulnetzEvent ev, bool isCurMonth = true)
    {
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
        var  color      = isPruefung ? _pruefungColor : _terminColor;
        bool isSelected = ev == _selectedEvent;

        // Hintergrund: gefüllt wenn ausgewählt, sonst leicht transparent
        Brush pillBg = isSelected
            ? new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B))
            : new SolidColorBrush(Color.FromArgb(30,  color.R, color.G, color.B));

        var pill = new WpfBorder
        {
            Background      = pillBg,
            BorderBrush     = new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(4, 1, 4, 1),
            Margin          = new Thickness(0, 1, 0, 1),
            Cursor          = WpfCursors.Hand,
            Opacity         = isCurMonth ? 1.0 : 0.5,
            Tag             = ev,
            IsHitTestVisible = true
        };

        var timePrefix = ev.IsAllDay ? "" : ev.Start.LocalDateTime.ToString("H:mm", _deCH) + " ";
        var text = new WpfTextBlock
        {
            Text         = timePrefix + ev.Summary,
            FontSize     = 10,
            Foreground   = isSelected
                ? WpfBrushes.White
                : new SolidColorBrush(Color.FromArgb(230, color.R, color.G, color.B)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        pill.Child = text;
        pill.MouseLeftButtonUp += EventPill_MouseUp;
        return pill;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Detail-Panel befüllen
    // ══════════════════════════════════════════════════════════════════════

    private void ShowDetailPanel(SchulnetzEvent ev)
    {
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
        var  color      = isPruefung ? _pruefungColor : _terminColor;
        var  label      = isPruefung ? "PRÜFUNG" : "TERMIN";

        TxtDetailBadge.Text     = label;
        TxtDetailBadge.Foreground = new SolidColorBrush(color);
        BtnDeleteFromApp.Tag    = ev;

        DetailContent.Children.Clear();

        // Titel
        DetailContent.Children.Add(new WpfTextBlock
        {
            Text        = ev.Summary,
            FontSize    = 18,
            FontWeight  = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin      = new Thickness(0, 0, 0, 18)
        });

        // Farbstreifen unter dem Titel
        DetailContent.Children.Add(new WpfBorder
        {
            Height              = 3,
            CornerRadius        = new CornerRadius(2),
            Background          = new SolidColorBrush(color),
            Margin              = new Thickness(0, 0, 0, 18),
            HorizontalAlignment = WpfHA.Left,
            Width               = 40
        });

        // Datum
        var dateStr = ev.Start.LocalDateTime.ToString("dddd, d. MMMM yyyy", _deCH);
        DetailContent.Children.Add(MakeDetailRow("📅", dateStr));

        // Zeit
        var timeStr = ev.IsAllDay
            ? "Ganztägig"
            : $"{ev.Start.LocalDateTime:HH:mm} – {ev.End.LocalDateTime:HH:mm} Uhr";
        DetailContent.Children.Add(MakeDetailRow("🕐", timeStr));

        // Ort
        if (!string.IsNullOrWhiteSpace(ev.Location))
            DetailContent.Children.Add(MakeDetailRow("📍", ev.Location!));

        // Typ-Erklärung
        var hint = isPruefung
            ? "Dieser Eintrag wurde als Prüfung klassifiziert."
            : "Dieser Eintrag wurde als Schultermin klassifiziert.";
        DetailContent.Children.Add(new WpfTextBlock
        {
            Text        = hint,
            FontSize    = 11,
            Opacity     = 0.45,
            TextWrapping = TextWrapping.Wrap,
            Margin      = new Thickness(0, 14, 0, 0)
        });

        // Panel einblenden
        DetailPanel.Visibility   = Visibility.Visible;
        DetailColumnDef.Width    = new GridLength(330);
    }

    private void HideDetailPanel()
    {
        _selectedEvent         = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailColumnDef.Width  = new GridLength(0);
    }

    private static UIElement MakeDetailRow(string icon, string text)
    {
        var row = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 10)
        };
        row.Children.Add(new WpfTextBlock
        {
            Text              = icon,
            FontSize          = 14,
            Margin            = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top
        });
        row.Children.Add(new WpfTextBlock
        {
            Text              = text,
            FontSize          = 13,
            TextWrapping      = TextWrapping.Wrap,
            Opacity           = 0.90,
            VerticalAlignment = VerticalAlignment.Top
        });
        return row;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Event-Handler
    // ══════════════════════════════════════════════════════════════════════

    private void EventPill_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfBorder pill && pill.Tag is SchulnetzEvent ev)
        {
            _selectedEvent = ev;
            _selectedDate  = ev.Start.Date;
            ShowDetailPanel(ev);
            BuildCalendar(FilteredEvents()); // Selektion aktualisieren
            e.Handled = true;               // Nicht an Zelle weitergeben
        }
    }

    private void DayCell_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfBorder border || border.Tag is not DateTime date) return;

        // Anderen Monat angewählt → dorthin navigieren
        if (date.Year != _year || date.Month != _month)
        {
            _year = date.Year; _month = date.Month;
        }

        // Gleichen Tag nochmal klicken → abwählen
        if (_selectedDate == date && _selectedEvent == null)
        {
            _selectedDate = null;
        }
        else
        {
            _selectedDate  = date;
            _selectedEvent = null;
            HideDetailPanel();
        }

        BuildCalendar(FilteredEvents());
    }

    private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
    {
        _selectedEvent = null;
        HideDetailPanel();
        BuildCalendar(FilteredEvents());
    }

    private void BtnDeleteFromApp_Click(object sender, RoutedEventArgs e)
    {
        if (BtnDeleteFromApp.Tag is not SchulnetzEvent ev) return;

        var result = MessageBox.Show(
            $"«{ev.Summary}» aus dem In-App-Kalender ausblenden?\n\n" +
            "Der Eintrag bleibt im Schulnetz-Feed und kehrt beim nächsten " +
            "«Feed laden» nicht mehr zurück, solange er ausgeblendet ist.",
            "Eintrag ausblenden",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        AppState.SuppressEvent(ev.Key); // Speichert + benachrichtigt → Refresh läuft via OnStateChanged
        HideDetailPanel();
    }

    private void BtnPrevMonth_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(-1);
        _year  = d.Year; _month = d.Month;
        _selectedDate  = null;
        _selectedEvent = null;
        HideDetailPanel();
        BuildCalendar(FilteredEvents());
    }

    private void BtnNextMonth_Click(object sender, RoutedEventArgs e)
    {
        var d = new DateTime(_year, _month, 1).AddMonths(1);
        _year  = d.Year; _month = d.Month;
        _selectedDate  = null;
        _selectedEvent = null;
        HideDetailPanel();
        BuildCalendar(FilteredEvents());
    }

    private void BtnToday_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        _year  = today.Year; _month = today.Month;
        _selectedDate  = today;
        _selectedEvent = null;
        HideDetailPanel();
        BuildCalendar(FilteredEvents());
    }

    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn) return;
        _filter        = btn.Tag as string ?? "All";
        _selectedDate  = null;
        _selectedEvent = null;
        HideDetailPanel();

        BtnFilterAll.Style      = (Style)FindResource(_filter == "All"      ? "PrimaryButton" : "SecondaryButton");
        BtnFilterPruefung.Style = (Style)FindResource(_filter == "Pruefung" ? "PrimaryButton" : "SecondaryButton");
        BtnFilterTermin.Style   = (Style)FindResource(_filter == "Termin"   ? "PrimaryButton" : "SecondaryButton");

        BuildCalendar(FilteredEvents());
    }
}
