using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchulnetzSync.Core.Model;

// WPF/WinForms-Ambiguität auflösen
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
    // ── Zustand ─────────────────────────────────────────────────────────────
    private int      _year;
    private int      _month;
    private DateTime? _selectedDate;
    private SchulnetzEvent? _selectedEvent;
    private string   _filter   = "All";
    private string   _viewMode = "Month"; // "Month" | "Week"

    private static readonly CultureInfo _deCH =
        CultureInfo.GetCultureInfo("de-CH");

    // Farben (konsistent im ganzen File)
    private static readonly Color _pruefungColor = Color.FromRgb(0xDC, 0x26, 0x26); // kräftiges Rot
    private static readonly Color _terminColor   = Color.FromRgb(0x25, 0x63, 0xEB); // kräftiges Blau
    private static readonly Color _accentColor   = Color.FromRgb(0x5C, 0x6E, 0xF7);

    // ── Init ─────────────────────────────────────────────────────────────────
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
    // Hauptrefresh
    // ══════════════════════════════════════════════════════════════════════
    private void Refresh()
    {
        bool hasData = AppState.CachedFeedEvents.Count > 0;
        NoDataHint.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        MainLayout.Visibility = hasData ? Visibility.Visible   : Visibility.Collapsed;
        if (!hasData) return;

        if (_viewMode == "Week")
            BuildWeekView();
        else
            BuildMonthView();
    }

    private IReadOnlyList<SchulnetzEvent> FilteredEvents()
    {
        var suppressed = AppState.SuppressedKeys;
        return AppState.CachedFeedEvents
            .Where(e => !suppressed.Contains(e.Key))
            .Where(e => _filter switch
            {
                "Pruefung" => e.Type == SchulnetzEventType.Pruefung,
                "Termin"   => e.Type == SchulnetzEventType.Termin,
                _          => true
            })
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    // MONATSANSICHT
    // ══════════════════════════════════════════════════════════════════════
    private void BuildMonthView()
    {
        MonthDayHeaders.Visibility = Visibility.Visible;
        CalendarGrid.Visibility    = Visibility.Visible;
        WeekGrid.Visibility        = Visibility.Collapsed;

        TxtMonthYear.Text = new DateTime(_year, _month, 1)
            .ToString("MMMM yyyy", _deCH);

        CalendarGrid.Children.Clear();

        var events      = FilteredEvents();
        var firstDay    = new DateTime(_year, _month, 1);
        int daysInMonth = DateTime.DaysInMonth(_year, _month);
        int startCol    = ((int)firstDay.DayOfWeek + 6) % 7; // Mo=0
        var today       = DateTime.Today;
        var prevFirst   = firstDay.AddMonths(-1);
        int prevDays    = DateTime.DaysInMonth(prevFirst.Year, prevFirst.Month);

        for (int ci = 0; ci < 42; ci++)
        {
            int row = ci / 7;
            int col = ci % 7;

            DateTime date;
            bool isCurMonth;
            if (ci < startCol)
            {
                date       = new DateTime(prevFirst.Year, prevFirst.Month, prevDays - startCol + ci + 1);
                isCurMonth = false;
            }
            else if (ci >= startCol + daysInMonth)
            {
                date       = firstDay.AddMonths(1).AddDays(ci - startCol - daysInMonth);
                isCurMonth = false;
            }
            else
            {
                date       = new DateTime(_year, _month, ci - startCol + 1);
                isCurMonth = true;
            }

            bool isToday   = date == today;
            bool isSel     = _selectedDate.HasValue && date == _selectedDate.Value;
            bool isWeekend = col >= 5;
            var dayEvents  = events.Where(e => e.Start.Date == date).ToList();

            var cell = MakeMonthCell(date, dayEvents, isCurMonth, isToday, isSel, isWeekend, row, col);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            CalendarGrid.Children.Add(cell);
        }
    }

    private UIElement MakeMonthCell(
        DateTime date, List<SchulnetzEvent> dayEvents,
        bool isCurMonth, bool isToday, bool isSelected,
        bool isWeekend, int row, int col)
    {
        // Zellen-Hintergrund
        Brush bg = (isToday && isCurMonth)
            ? new SolidColorBrush(Color.FromArgb(22, _accentColor.R, _accentColor.G, _accentColor.B))
            : isSelected
            ? new SolidColorBrush(Color.FromArgb(28, _accentColor.R, _accentColor.G, _accentColor.B))
            : (!isCurMonth || isWeekend)
            ? new SolidColorBrush(Color.FromArgb(14, 0x80, 0x80, 0x80))
            : WpfBrushes.Transparent;

        var gridLine = new SolidColorBrush(Color.FromArgb(45, 0x80, 0x80, 0x80));
        var cell = new WpfBorder
        {
            Background      = bg,
            BorderBrush     = gridLine,
            BorderThickness = new Thickness(col == 0 ? 1 : 0, 0, 1, 1),
            Tag             = date,
            Cursor          = WpfCursors.Hand
        };
        // KEIN IsHitTestVisible=false hier — Klick-Events müssen zu den Pills durchkommen!
        cell.MouseLeftButtonUp += DayCell_MouseUp;

        var cellPanel = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };

        // ── Tagesdatum oben links ──
        if (isToday && isCurMonth)
        {
            // Heute: weisse Zahl in farbigem Kreis
            var circle = new WpfBorder
            {
                Width               = 26,
                Height              = 26,
                CornerRadius        = new CornerRadius(13),
                Background          = new SolidColorBrush(_accentColor),
                HorizontalAlignment = WpfHA.Left,
                Margin              = new Thickness(2, 0, 0, 3),
                Child = new WpfTextBlock
                {
                    Text                = date.Day.ToString(),
                    FontSize            = 12,
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
            var numColor = isCurMonth ? (isWeekend ? "#A0A8B8" : "#C8D0E0") : "#505868";
            cellPanel.Children.Add(new WpfTextBlock
            {
                Text                = date.Day.ToString(),
                FontSize            = 12,
                FontWeight          = FontWeights.SemiBold,
                Foreground          = new SolidColorBrush(
                                          (Color)System.Windows.Media.ColorConverter.ConvertFromString(numColor)),
                HorizontalAlignment = WpfHA.Left,
                Margin              = new Thickness(3, 0, 0, 3)
            });
        }

        // ── Event-Pills (max. 3, dann "+N mehr") ──
        const int maxPills = 3;
        for (int i = 0; i < Math.Min(dayEvents.Count, maxPills); i++)
            cellPanel.Children.Add(MakeMonthPill(dayEvents[i]));

        if (dayEvents.Count > maxPills)
        {
            cellPanel.Children.Add(new WpfTextBlock
            {
                Text     = $"+{dayEvents.Count - maxPills} mehr",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB8)),
                Margin   = new Thickness(3, 1, 0, 0)
            });
        }

        cell.Child = cellPanel;
        return cell;
    }

    /// <summary>Farbige Pill (Outlook-Stil: kräftiger Hintergrund, weisser Text).</summary>
    private UIElement MakeMonthPill(SchulnetzEvent ev)
    {
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
        var  color      = isPruefung ? _pruefungColor : _terminColor;
        bool isSel      = ev == _selectedEvent;

        // Kräftiger Hintergrund (wie Google Calendar / Outlook dark mode)
        byte bgAlpha = isSel ? (byte)240 : (byte)195;
        var bg = new SolidColorBrush(Color.FromArgb(bgAlpha, color.R, color.G, color.B));

        var pill = new WpfBorder
        {
            Background   = bg,
            CornerRadius = new CornerRadius(3),
            Padding      = new Thickness(4, 1, 4, 2),
            Margin       = new Thickness(0, 1, 0, 1),
            Cursor       = WpfCursors.Hand,
            Tag          = ev
            // KEIN IsHitTestVisible=false — das war der Bug!
        };

        var timePrefix = ev.IsAllDay ? "" : ev.Start.LocalDateTime.ToString("H:mm ", _deCH);
        pill.Child = new WpfTextBlock
        {
            Text         = timePrefix + ev.Summary,
            FontSize     = 10,
            FontWeight   = isPruefung ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground   = WpfBrushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        pill.MouseLeftButtonUp += EventPill_MouseUp;
        return pill;
    }

    // ══════════════════════════════════════════════════════════════════════
    // WOCHENANSICHT
    // ══════════════════════════════════════════════════════════════════════
    private void BuildWeekView()
    {
        MonthDayHeaders.Visibility = Visibility.Collapsed;
        CalendarGrid.Visibility    = Visibility.Collapsed;
        WeekGrid.Visibility        = Visibility.Visible;
        WeekGrid.Children.Clear();

        // Wochenstart (Mo) für das Anker-Datum ermitteln
        var anchor     = _selectedDate ?? DateTime.Today;
        int daysFromMo = ((int)anchor.DayOfWeek + 6) % 7;
        var weekStart  = anchor.AddDays(-daysFromMo); // Montag
        var weekEnd    = weekStart.AddDays(6);         // Sonntag

        // Monatslabel aktualisieren
        TxtMonthYear.Text = weekStart.Month == weekEnd.Month
            ? $"{weekStart:d. MMM} – {weekEnd:d. MMM yyyy}"
            : $"{weekStart.ToString("d. MMM", _deCH)} – {weekEnd.ToString("d. MMM yyyy", _deCH)}";

        var events = FilteredEvents();
        var today  = DateTime.Today;
        var gridLine = new SolidColorBrush(Color.FromArgb(45, 0x80, 0x80, 0x80));

        for (int col = 0; col < 7; col++)
        {
            var date      = weekStart.AddDays(col);
            var dayEvents = events.Where(e => e.Start.Date == date)
                                  .OrderBy(e => e.Start).ToList();
            bool isToday  = date == today;
            bool isWeekend = col >= 5;

            // Spalten-Container
            var column = new WpfBorder
            {
                BorderBrush     = gridLine,
                BorderThickness = new Thickness(col == 0 ? 1 : 0, 0, 1, 0)
            };

            var colGrid = new Grid();
            colGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            colGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── Spaltenheader: Wochentag + Datum ──
            Brush headerBg = isToday
                ? new SolidColorBrush(Color.FromArgb(30, _accentColor.R, _accentColor.G, _accentColor.B))
                : isWeekend
                ? new SolidColorBrush(Color.FromArgb(14, 0x80, 0x80, 0x80))
                : WpfBrushes.Transparent;

            var header = new WpfBorder
            {
                Background      = headerBg,
                BorderBrush     = gridLine,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(0, 10, 0, 10)
            };

            var dayLabel = new WpfTextBlock
            {
                Text                = date.ToString("ddd", _deCH).ToUpper(),
                FontSize            = 10,
                FontWeight          = FontWeights.SemiBold,
                Opacity             = isWeekend ? 0.40 : 0.55,
                HorizontalAlignment = WpfHA.Center,
                Margin              = new Thickness(0, 0, 0, 3)
            };

            Brush numFg = isToday
                ? new SolidColorBrush(_accentColor)
                : new SolidColorBrush(Color.FromRgb(isWeekend ? (byte)0x70 : (byte)0xC0,
                                                     isWeekend ? (byte)0x78 : (byte)0xC8,
                                                     isWeekend ? (byte)0x88 : (byte)0xE0));
            var dayNum = new WpfTextBlock
            {
                Text                = date.Day.ToString(),
                FontSize            = 24,
                FontWeight          = isToday ? FontWeights.Bold : FontWeights.Normal,
                Foreground          = numFg,
                HorizontalAlignment = WpfHA.Center
            };

            var headerPanel = new StackPanel { HorizontalAlignment = WpfHA.Center };
            headerPanel.Children.Add(dayLabel);
            headerPanel.Children.Add(dayNum);
            header.Child = headerPanel;
            Grid.SetRow(header, 0);
            colGrid.Children.Add(header);

            // ── Events der Spalte (scrollbar) ──
            var eventsPanel = new StackPanel { Margin = new Thickness(5, 6, 5, 6) };

            if (dayEvents.Count == 0)
            {
                eventsPanel.Children.Add(new WpfTextBlock
                {
                    Text                = "–",
                    FontSize            = 13,
                    Opacity             = 0.22,
                    HorizontalAlignment = WpfHA.Center,
                    Margin              = new Thickness(0, 16, 0, 0)
                });
            }
            else
            {
                foreach (var ev in dayEvents)
                    eventsPanel.Children.Add(MakeWeekEventCard(ev));
            }

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = eventsPanel
            };
            Grid.SetRow(scroll, 1);
            colGrid.Children.Add(scroll);

            column.Child = column.Child = colGrid;
            Grid.SetColumn(column, col);
            WeekGrid.Children.Add(column);
        }
    }

    /// <summary>Event-Karte in der Wochenansicht: breiter, mit Zeit und Location.</summary>
    private UIElement MakeWeekEventCard(SchulnetzEvent ev)
    {
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
        var  color      = isPruefung ? _pruefungColor : _terminColor;
        bool isSel      = ev == _selectedEvent;

        var card = new WpfBorder
        {
            Background      = new SolidColorBrush(
                                  Color.FromArgb(isSel ? (byte)200 : (byte)160,
                                                 color.R, color.G, color.B)),
            CornerRadius    = new CornerRadius(5),
            Padding         = new Thickness(8, 6, 8, 6),
            Margin          = new Thickness(0, 0, 0, 4),
            Cursor          = WpfCursors.Hand,
            Tag             = ev
        };

        var timeStr = ev.IsAllDay
            ? "Ganztägig"
            : $"{ev.Start.LocalDateTime:H:mm} – {ev.End.LocalDateTime:H:mm}";

        var inner = new StackPanel();
        inner.Children.Add(new WpfTextBlock
        {
            Text       = timeStr,
            FontSize   = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            Margin     = new Thickness(0, 0, 0, 2)
        });
        inner.Children.Add(new WpfTextBlock
        {
            Text        = ev.Summary,
            FontSize    = 11,
            FontWeight  = FontWeights.SemiBold,
            Foreground  = WpfBrushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(ev.Location))
        {
            inner.Children.Add(new WpfTextBlock
            {
                Text      = "📍 " + ev.Location,
                FontSize  = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                Margin    = new Thickness(0, 2, 0, 0)
            });
        }

        card.Child = inner;
        card.MouseLeftButtonUp += EventPill_MouseUp;
        return card;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Detail-Panel
    // ══════════════════════════════════════════════════════════════════════
    private void ShowDetailPanel(SchulnetzEvent ev)
    {
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
        var  color      = isPruefung ? _pruefungColor : _terminColor;

        TxtDetailBadge.Text       = isPruefung ? "⚠  PRÜFUNG" : "📌  TERMIN";
        TxtDetailBadge.Foreground = new SolidColorBrush(color);
        BtnDeleteFromApp.Tag      = ev;

        DetailContent.Children.Clear();

        // Farbstreifen
        DetailContent.Children.Add(new WpfBorder
        {
            Height              = 4,
            CornerRadius        = new CornerRadius(2),
            Background          = new SolidColorBrush(color),
            Margin              = new Thickness(0, 0, 0, 14),
            HorizontalAlignment = WpfHA.Left,
            Width               = 48
        });

        // Titel
        DetailContent.Children.Add(new WpfTextBlock
        {
            Text         = ev.Summary,
            FontSize     = 17,
            FontWeight   = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 16)
        });

        // Datum
        DetailContent.Children.Add(MakeDetailRow("📅",
            ev.Start.LocalDateTime.ToString("dddd, d. MMMM yyyy", _deCH)));

        // Zeit
        var timeStr = ev.IsAllDay
            ? "Ganztägig"
            : $"{ev.Start.LocalDateTime:HH:mm} – {ev.End.LocalDateTime:HH:mm} Uhr";
        DetailContent.Children.Add(MakeDetailRow("🕐", timeStr));

        // Ort
        if (!string.IsNullOrWhiteSpace(ev.Location))
            DetailContent.Children.Add(MakeDetailRow("📍", ev.Location!));

        // Typ-Hinweis
        DetailContent.Children.Add(new WpfTextBlock
        {
            Text         = isPruefung
                           ? "Als Prüfung klassifiziert."
                           : "Als Schultermin klassifiziert.",
            FontSize     = 11,
            Opacity      = 0.40,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 12, 0, 0)
        });

        DetailPanel.Visibility  = Visibility.Visible;
        DetailColumnDef.Width   = new GridLength(320);
    }

    private void HideDetailPanel()
    {
        _selectedEvent          = null;
        DetailPanel.Visibility  = Visibility.Collapsed;
        DetailColumnDef.Width   = new GridLength(0);
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
            Refresh(); // Selektion-Highlight neu zeichnen
            e.Handled = true;
        }
    }

    private void DayCell_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfBorder border || border.Tag is not DateTime date) return;

        // Anderen Monat → dorthin navigieren
        if (date.Year != _year || date.Month != _month)
        {
            _year = date.Year; _month = date.Month;
        }

        // Gleichen Tag nochmal → abwählen
        if (_selectedDate == date && _selectedEvent == null)
            _selectedDate = null;
        else
        {
            _selectedDate  = date;
            _selectedEvent = null;
            HideDetailPanel();
        }

        Refresh();
    }

    private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
    {
        HideDetailPanel();
        Refresh();
    }

    private void BtnDeleteFromApp_Click(object sender, RoutedEventArgs e)
    {
        if (BtnDeleteFromApp.Tag is not SchulnetzEvent ev) return;

        var result = MessageBox.Show(
            $"«{ev.Summary}» aus dem In-App-Kalender dauerhaft ausblenden?\n\n" +
            "Der Eintrag bleibt im Schulnetz-Feed unverändert.",
            "Eintrag ausblenden",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;
        AppState.SuppressEvent(ev.Key); // löst Notify → Refresh aus
        HideDetailPanel();
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    private void BtnPrevMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == "Week")
        {
            // Wochenweise navigieren
            var anchor = (_selectedDate ?? DateTime.Today).AddDays(-7);
            int daysFromMo = ((int)anchor.DayOfWeek + 6) % 7;
            _selectedDate = anchor.AddDays(-daysFromMo); // Montag der Vorwoche
            _year = _selectedDate.Value.Year; _month = _selectedDate.Value.Month;
        }
        else
        {
            var d = new DateTime(_year, _month, 1).AddMonths(-1);
            _year = d.Year; _month = d.Month;
            _selectedDate = null;
        }
        _selectedEvent = null;
        HideDetailPanel();
        Refresh();
    }

    private void BtnNextMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == "Week")
        {
            var anchor = (_selectedDate ?? DateTime.Today).AddDays(7);
            int daysFromMo = ((int)anchor.DayOfWeek + 6) % 7;
            _selectedDate = anchor.AddDays(-daysFromMo);
            _year = _selectedDate.Value.Year; _month = _selectedDate.Value.Month;
        }
        else
        {
            var d = new DateTime(_year, _month, 1).AddMonths(1);
            _year = d.Year; _month = d.Month;
            _selectedDate = null;
        }
        _selectedEvent = null;
        HideDetailPanel();
        Refresh();
    }

    private void BtnToday_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        _year  = today.Year; _month = today.Month;
        _selectedDate  = today;
        _selectedEvent = null;
        HideDetailPanel();
        Refresh();
    }

    // ── Ansicht (Monat / Woche) ──────────────────────────────────────────────

    private void BtnViewMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewMode = "Month";
        BtnViewMonth.Style = (Style)FindResource("PrimaryButton");
        BtnViewWeek.Style  = (Style)FindResource("SecondaryButton");
        Refresh();
    }

    private void BtnViewWeek_Click(object sender, RoutedEventArgs e)
    {
        _viewMode = "Week";
        BtnViewWeek.Style  = (Style)FindResource("PrimaryButton");
        BtnViewMonth.Style = (Style)FindResource("SecondaryButton");
        // Sicherstellen dass eine Woche ausgewählt ist
        if (!_selectedDate.HasValue)
            _selectedDate = DateTime.Today;
        Refresh();
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn) return;
        _filter        = btn.Tag as string ?? "All";
        _selectedEvent = null;
        HideDetailPanel();

        BtnFilterAll.Style      = (Style)FindResource(_filter == "All"      ? "PrimaryButton" : "SecondaryButton");
        BtnFilterPruefung.Style = (Style)FindResource(_filter == "Pruefung" ? "PrimaryButton" : "SecondaryButton");
        BtnFilterTermin.Style   = (Style)FindResource(_filter == "Termin"   ? "PrimaryButton" : "SecondaryButton");

        Refresh();
    }
}
