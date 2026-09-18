using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchulnetzSync.Core.Colors;
using SchulnetzSync.Core.Model;
using SchulnetzSync.UI.Controls;
using SchulnetzSync.UI.Model;
using ModernWpf.Controls.Primitives;
using Point          = System.Windows.Point;

// Pin down the WPF versions of names WinForms also uses
using WpfBorder      = System.Windows.Controls.Border;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfHA          = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPage        = System.Windows.Controls.Page;
using WpfTextBlock   = System.Windows.Controls.TextBlock;
using TextBox        = System.Windows.Controls.TextBox;
using WpfWrapPanel   = System.Windows.Controls.WrapPanel;

namespace SchulnetzSync.UI.Pages;

/// <summary>
/// The calendar: a month grid, a week timetable, and a side panel that shows the selected
/// entry, the settings or the form for a new hand-made event.
///
/// Everything is drawn in code rather than bound in XAML. The layout depends on overlaps,
/// colours and the time grid in ways that would be worse to express as bindings, and the
/// page rebuilds itself from <see cref="AppState"/> after every change anyway.
/// </summary>
public partial class EventsPage : WpfPage
{
    // ── State ───────────────────────────────────────────────────────────────
    private int       _year;
    private int       _month;
    private DateTime? _selectedDate;
    private SchulnetzEvent? _selectedEvent;
    private string    _filter    = "All";
    private string    _viewMode  = "Week";     // "Month" | "Week"
    private string    _panelMode = "None";     // "None" | "Detail" | "Settings" | "AddEvent"

    // Geometry of the week grid
    private const int    _weekStartH = 7;      // 07:00
    private const int    _weekEndH   = 22;     // 22:00
    private const int    _slotMin    = 15;     // minutes per row
    private const double _slotPx     = 15.0;  // pixels per row, so an hour is 60 px
    private const double _gutterW    = 48.0;  // width of the time column on the left

    private static readonly CultureInfo _deCH = CultureInfo.GetCultureInfo("de-CH");

    // Defaults; CategoryColors overrides them
    private static readonly Color _pruefungColor = Color.FromRgb(0xDC, 0x26, 0x26); // red
    private static readonly Color _terminColor   = Color.FromRgb(0xD9, 0x77, 0x06); // amber
    private static readonly Color _lektionColor  = Color.FromRgb(0x25, 0x63, 0xEB); // blue
    private static readonly Color _accentColor   = Color.FromRgb(0x5C, 0x6E, 0xF7);

    // The same palette the task lists use
    private static readonly (string Hex, string Name)[] _palette = ColorPalette.All;

    // ── Init ─────────────────────────────────────────────────────────────────
    public EventsPage()
    {
        InitializeComponent();
        var today = DateTime.Today;
        _year  = today.Year;
        _month = today.Month;

        Loaded   += (_, _) =>
        {
            AppState.Changed += OnStateChanged;
            if (!_selectedDate.HasValue) _selectedDate = DateTime.Today;
            Refresh();
        };
        Unloaded += (_, _) => AppState.Changed -= OnStateChanged;
    }

    private void OnStateChanged() => Dispatcher.Invoke(Refresh);

    // ── Colour helpers ───────────────────────────────────────────────────────

    /// <summary>Works out which colour key an event belongs to.</summary>
    private static string GetColorKey(SchulnetzEvent ev)
    {
        if (EventKeys.IsManual(ev.Key))
            return ev.Type == SchulnetzEventType.Pruefung ? "Pruefung" : "Termin";
        return ev.Type switch
        {
            SchulnetzEventType.Pruefung => "Pruefung",
            SchulnetzEventType.Termin   => "Termin",
            _                           => SubjectCode.FromSummary(ev.Summary)
        };
    }

    /// <summary>
    /// The subject whose comments an entry shows: lessons and exams out of the feed.
    /// Appointments and hand-made entries belong to no subject at all.
    /// </summary>
    private static string? CommentSubjectOf(SchulnetzEvent ev)
    {
        if (EventKeys.IsManual(ev.Key)) return null;
        if (ev.Type is not (SchulnetzEventType.Lektion or SchulnetzEventType.Pruefung)) return null;
        var code = SubjectCode.Split(ev.Summary).Code;
        return code.Length == 0 ? null : code;
    }

    /// <summary>
    /// Short form for the calendar tiles: "TEU" instead of "TEU_I26A_SmiJa". Class and
    /// teacher are the same on every entry and only eat space; the detail panel still
    /// shows the full title.
    /// </summary>
    private static (string Title, string Detail) ShortTitle(SchulnetzEvent ev)
    {
        var (code, rest) = SubjectCode.Split(ev.Summary);
        return code.Length == 0 ? (ev.Summary, "") : (code, rest);
    }

    /// <summary>The colour an event actually gets: individual, then subject or category, then default.</summary>
    private Color GetEventColor(SchulnetzEvent ev)
    {
        var key = GetColorKey(ev);
        var hex = AppState.GetEventColor(ev.Key, key);
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch
        {
            return ev.Type switch
            {
                SchulnetzEventType.Pruefung => _pruefungColor,
                SchulnetzEventType.Lektion  => _lektionColor,
                _                           => _terminColor
            };
        }
    }

    /// <summary>The colour behind a key, without the fallback chain.</summary>
    private static Color GetColorForKey(string key)
    {
        var hex = AppState.GetEventColor(key);
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch { return key == "Pruefung" ? Color.FromRgb(0xDC, 0x26, 0x26)
                     : key == "Termin"   ? Color.FromRgb(0xD9, 0x77, 0x06)
                     :                     Color.FromRgb(0x25, 0x63, 0xEB); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Rebuilding the view
    // ══════════════════════════════════════════════════════════════════════
    private void Refresh()
    {
        bool hasData = AppState.CachedFeedEvents.Count > 0 || AppState.ManualEvents.Count > 0;
        NoDataHint.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        MainLayout.Visibility = hasData ? Visibility.Visible   : Visibility.Collapsed;
        if (!hasData) return;

        if (_viewMode == "Week") BuildWeekView();
        else BuildMonthView();
    }

    private IReadOnlyList<SchulnetzEvent> FilteredEvents()
    {
        var suppressed = AppState.SuppressedKeys;

        var feedEvents = AppState.CachedFeedEvents
            .Where(e => !suppressed.Contains(e.Key) && MatchFilter(e));

        var manualEvents = AppState.ManualAsEvents()
            .Where(e => !suppressed.Contains(e.Key) && MatchFilter(e));

        return feedEvents.Concat(manualEvents).ToList();
    }

    private bool MatchFilter(SchulnetzEvent e) => _filter switch
    {
        "Pruefung" => e.Type == SchulnetzEventType.Pruefung,
        "Termin"   => e.Type == SchulnetzEventType.Termin,
        "Lektion"  => e.Type == SchulnetzEventType.Lektion,
        _          => true
    };

    // ══════════════════════════════════════════════════════════════════════
    // MONTH VIEW
    // ══════════════════════════════════════════════════════════════════════
    private void BuildMonthView()
    {
        MonthDayHeaders.Visibility = Visibility.Visible;
        CalendarGrid.Visibility    = Visibility.Visible;
        WeekGrid.Visibility        = Visibility.Collapsed;

        TxtMonthYear.Text = new DateTime(_year, _month, 1).ToString("MMMM yyyy", _deCH);
        CalendarGrid.Children.Clear();

        var events      = FilteredEvents();
        var firstDay    = new DateTime(_year, _month, 1);
        int daysInMonth = DateTime.DaysInMonth(_year, _month);
        int startCol    = ((int)firstDay.DayOfWeek + 6) % 7; // shift so Monday is 0
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

            var cell = MakeMonthCell(date, dayEvents, isCurMonth, isToday, isSel, isWeekend, col);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            CalendarGrid.Children.Add(cell);
        }
    }

    private UIElement MakeMonthCell(
        DateTime date, List<SchulnetzEvent> dayEvents,
        bool isCurMonth, bool isToday, bool isSelected,
        bool isWeekend, int col)
    {
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
        cell.MouseLeftButtonUp += DayCell_MouseUp;

        var cellPanel = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };

        // ── Day number, dimmed by opacity rather than a fixed colour ──
        if (isToday && isCurMonth)
        {
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
            // Opacity instead of a colour, so it follows the light and dark theme by itself
            cellPanel.Children.Add(new WpfTextBlock
            {
                Text                = date.Day.ToString(),
                FontSize            = 12,
                FontWeight          = FontWeights.SemiBold,
                Opacity             = isCurMonth ? (isWeekend ? 0.50 : 0.85) : 0.28,
                HorizontalAlignment = WpfHA.Left,
                Margin              = new Thickness(3, 0, 0, 3)
            });
        }

        // ── Event pills, three at most, then "+N mehr" ──
        const int maxPills = 3;
        for (int i = 0; i < Math.Min(dayEvents.Count, maxPills); i++)
            cellPanel.Children.Add(MakeMonthPill(dayEvents[i]));

        if (dayEvents.Count > maxPills)
        {
            cellPanel.Children.Add(new WpfTextBlock
            {
                Text     = $"+{dayEvents.Count - maxPills} mehr",
                FontSize = 9,
                Opacity  = 0.55,
                Margin   = new Thickness(3, 1, 0, 0)
            });
        }

        cell.Child = cellPanel;
        return cell;
    }

    private UIElement MakeMonthPill(SchulnetzEvent ev)
    {
        var  color = GetEventColor(ev);
        bool isSel = ev == _selectedEvent;

        byte bgAlpha = isSel ? (byte)240 : (byte)210;
        var bg = new SolidColorBrush(Color.FromArgb(bgAlpha, color.R, color.G, color.B));

        var pill = new WpfBorder
        {
            Background   = bg,
            CornerRadius = new CornerRadius(3),
            Padding      = new Thickness(4, 1, 4, 2),
            Margin       = new Thickness(0, 1, 0, 1),
            Cursor       = WpfCursors.Hand,
            Tag          = ev
        };

        var timePrefix    = ev.IsAllDay ? "" : ev.Start.LocalDateTime.ToString("H:mm ", _deCH);
        bool isPruefung   = ev.Type == SchulnetzEventType.Pruefung;
        var (title, rest) = ShortTitle(ev);
        pill.ToolTip      = ev.Summary;
        pill.Child = new WpfTextBlock
        {
            Text         = timePrefix + title + (rest.Length > 0 ? " · " + rest : ""),
            FontSize     = 10,
            FontWeight   = isPruefung ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground   = WpfBrushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        pill.MouseLeftButtonUp += EventPill_MouseUp;
        return pill;
    }

    // ══════════════════════════════════════════════════════════════════════
    // WEEK VIEW — the timetable grid
    // ══════════════════════════════════════════════════════════════════════
    private void BuildWeekView()
    {
        MonthDayHeaders.Visibility = Visibility.Collapsed;
        CalendarGrid.Visibility    = Visibility.Collapsed;
        WeekGrid.Visibility        = Visibility.Visible;

        // Wipe the previous children and definitions
        WeekGrid.Children.Clear();
        WeekGrid.ColumnDefinitions.Clear();
        WeekGrid.RowDefinitions.Clear();

        var anchor    = _selectedDate ?? DateTime.Today;
        int fromMon   = ((int)anchor.DayOfWeek + 6) % 7;
        var weekStart = anchor.AddDays(-fromMon);
        var weekEnd   = weekStart.AddDays(6);
        var today     = DateTime.Today;
        var events    = FilteredEvents();
        var gridLine  = new SolidColorBrush(Color.FromArgb(40, 0x80, 0x80, 0x80));

        int kw = System.Globalization.ISOWeek.GetWeekOfYear(weekStart);
        TxtMonthYear.Text = $"KW {kw}  ·  {weekStart.ToString("d. MMM", _deCH)} – {weekEnd.ToString("d. MMM yyyy", _deCH)}";

        // Two rows: the day header on top, the scrolling grid below
        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── Header row with the weekday numbers ──────────────────────────────
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_gutterW) });
        for (int d = 0; d < 7; d++)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Spacer above the time column
        var gutterSpacer = new WpfBorder { BorderBrush = gridLine, BorderThickness = new Thickness(0, 0, 1, 1) };
        Grid.SetColumn(gutterSpacer, 0);
        headerGrid.Children.Add(gutterSpacer);

        for (int d = 0; d < 7; d++)
        {
            var date      = weekStart.AddDays(d);
            bool isToday  = date == today;
            bool isWe     = d >= 5;

            Brush headerBg = isToday
                ? new SolidColorBrush(Color.FromArgb(28, _accentColor.R, _accentColor.G, _accentColor.B))
                : WpfBrushes.Transparent;

            var hdr = new WpfBorder
            {
                Background      = headerBg,
                BorderBrush     = gridLine,
                BorderThickness = new Thickness(0, 0, d < 6 ? 1 : 0, 1),
                Padding         = new Thickness(0, 8, 0, 8)
            };

            UIElement dayNum;
            if (isToday)
            {
                dayNum = new WpfBorder
                {
                    Width               = 30,
                    Height              = 30,
                    CornerRadius        = new CornerRadius(15),
                    Background          = new SolidColorBrush(_accentColor),
                    HorizontalAlignment = WpfHA.Center,
                    Child               = new WpfTextBlock
                    {
                        Text                = date.Day.ToString(),
                        FontSize            = 14,
                        FontWeight          = FontWeights.Bold,
                        Foreground          = WpfBrushes.White,
                        HorizontalAlignment = WpfHA.Center,
                        VerticalAlignment   = VerticalAlignment.Center
                    }
                };
            }
            else
            {
                dayNum = new WpfTextBlock
                {
                    Text                = date.Day.ToString(),
                    FontSize            = 18,
                    FontWeight          = FontWeights.Normal,
                    Opacity             = isWe ? 0.40 : 0.85,
                    HorizontalAlignment = WpfHA.Center
                };
            }

            var hp = new StackPanel { HorizontalAlignment = WpfHA.Center };
            hp.Children.Add(new WpfTextBlock
            {
                Text                = date.ToString("ddd", _deCH).ToUpper(),
                FontSize            = 10,
                FontWeight          = FontWeights.SemiBold,
                Opacity             = isWe ? 0.35 : 0.50,
                HorizontalAlignment = WpfHA.Center,
                Margin              = new Thickness(0, 0, 0, 2)
            });
            hp.Children.Add(dayNum);
            hdr.Child = hp;
            Grid.SetColumn(hdr, d + 1);
            headerGrid.Children.Add(hdr);
        }

        Grid.SetRow(headerGrid, 0);
        Grid.SetColumn(headerGrid, 0);
        WeekGrid.Children.Add(headerGrid);

        // ── The scrollable time grid ─────────────────────────────────────────
        int totalSlots = (_weekEndH - _weekStartH) * (60 / _slotMin); // 60 rows at 15 min each, 900 px tall

        var tGrid = new Grid();
        tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_gutterW) });
        for (int d = 0; d < 7; d++)
            tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i <= totalSlots; i++)
            tGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_slotPx) });

        // Hour lines and the labels down the left
        int hourCount = _weekEndH - _weekStartH;
        for (int h = 0; h <= hourCount; h++)
        {
            int slotRow = h * (60 / _slotMin);
            int hour    = _weekStartH + h;

            // Line across every day column
            var hline = new WpfBorder
            {
                Height            = 1,
                Background        = new SolidColorBrush(Color.FromArgb(h == 0 ? (byte)70 : (byte)35, 0x80, 0x80, 0x80)),
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible  = false
            };
            Grid.SetRow(hline, slotRow);
            Grid.SetColumn(hline, 1);
            Grid.SetColumnSpan(hline, 7);
            tGrid.Children.Add(hline);

            // Half-hour line, fainter
            if (h < hourCount)
            {
                int halfSlot = slotRow + (30 / _slotMin);
                var hHalf = new WpfBorder
                {
                    Height            = 1,
                    Background        = new SolidColorBrush(Color.FromArgb(18, 0x80, 0x80, 0x80)),
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible  = false
                };
                Grid.SetRow(hHalf, halfSlot);
                Grid.SetColumn(hHalf, 1);
                Grid.SetColumnSpan(hHalf, 7);
                tGrid.Children.Add(hHalf);
            }

            // Time label on the left. The first hour (07:00) must not be pulled up by the
            // negative margin, or it gets clipped at the top edge.
            if (h < hourCount)
            {
                var lbl = new WpfTextBlock
                {
                    Text                = $"{hour:D2}:00",
                    FontSize            = 9,
                    Opacity             = 0.42,
                    VerticalAlignment   = VerticalAlignment.Top,
                    HorizontalAlignment = WpfHA.Right,
                    Margin              = new Thickness(0, h == 0 ? 2 : -6, 5, 0),
                    IsHitTestVisible    = false
                };
                Grid.SetRow(lbl, slotRow);
                Grid.SetColumn(lbl, 0);
                tGrid.Children.Add(lbl);
            }
        }

        // Vertical dividers between the days
        for (int d = 0; d < 7; d++)
        {
            var vline = new WpfBorder
            {
                BorderBrush       = gridLine,
                BorderThickness   = new Thickness(d == 0 ? 1 : 0, 0, 1, 0),
                IsHitTestVisible  = false
            };
            Grid.SetRow(vline, 0);
            Grid.SetRowSpan(vline, totalSlots + 1);
            Grid.SetColumn(vline, d + 1);
            tGrid.Children.Add(vline);

            // Tint the background for weekends and for today
            var date      = weekStart.AddDays(d);
            bool isToday  = date == today;
            bool isWe     = d >= 5;
            if (isToday || isWe)
            {
                var dayBg = new WpfBorder
                {
                    Background       = isToday
                        ? new SolidColorBrush(Color.FromArgb(10, _accentColor.R, _accentColor.G, _accentColor.B))
                        : new SolidColorBrush(Color.FromArgb(8, 0x80, 0x80, 0x80)),
                    IsHitTestVisible = false
                };
                Grid.SetRow(dayBg, 0);
                Grid.SetRowSpan(dayBg, totalSlots + 1);
                Grid.SetColumn(dayBg, d + 1);
                tGrid.Children.Add(dayBg);
            }
        }

        // Draw the events into the day columns
        double containerH = (totalSlots + 1) * _slotPx; // full height of the grid in pixels

        for (int d = 0; d < 7; d++)
        {
            var date      = weekStart.AddDays(d);
            var dayEvents = events.Where(e => e.Start.Date == date).OrderBy(e => e.Start).ToList();

            // All-day entries become a compact strip at the top of the day
            int allDayRow = 0;
            foreach (var ev in dayEvents.Where(e => e.IsAllDay))
            {
                var strip = MakeWeekAllDayStrip(ev);
                Grid.SetRow(strip, allDayRow);
                Grid.SetColumn(strip, d + 1);
                tGrid.Children.Add(strip);
                allDayRow = Math.Min(allDayRow + 1, totalSlots - 1);
            }

            // Timed entries, with overlap detection
            var timedEvents = dayEvents.Where(e => !e.IsAllDay).ToList();
            if (timedEvents.Count == 0) continue;

            // Work out column, span and maxCols per event
            var colMap  = AssignEventColumns(timedEvents);
            int maxCols = colMap.Values.Any() ? colMap.Values.Max(v => v.TotalCols) : 1;

            // One grid per day spanning every row, with sub-columns for the overlaps
            var dayGrid = new Grid { IsHitTestVisible = true };
            for (int c = 0; c < maxCols; c++)
                dayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            foreach (var ev in timedEvents)
            {
                var local    = ev.Start.LocalDateTime;
                var localEnd = ev.End.LocalDateTime;
                double startH = local.Hour   + local.Minute   / 60.0;
                double endH   = localEnd.Hour + localEnd.Minute / 60.0;

                // Outside the visible grid, so skip it
                if (endH <= _weekStartH || startH >= _weekEndH) continue;
                startH = Math.Max(startH, _weekStartH);
                endH   = Math.Min(endH,   _weekEndH);

                // Position in pixels inside the day grid
                double topPx   = (startH - _weekStartH) * (60.0 / _slotMin) * _slotPx;
                double cardH   = Math.Max(4.0, (endH - startH) * (60.0 / _slotMin) * _slotPx - 2.0);

                var (col, span, _) = colMap.TryGetValue(ev, out var t) ? t : (0, maxCols, maxCols);

                // Gaps: 2 px at the outer edges, 1 px between columns
                double mLeft  = col == 0         ? 2.0 : 1.0;
                double mRight = col + span == maxCols ? 2.0 : 1.0;

                var card = MakeWeekEventCard(ev);
                card.Margin            = new Thickness(mLeft, topPx + 1.0, mRight, 0);
                card.Height            = cardH;
                card.VerticalAlignment = VerticalAlignment.Top;

                Grid.SetColumn(card, col);
                Grid.SetColumnSpan(card, span);
                dayGrid.Children.Add(card);
            }

            // Let the day grid span every row of the time grid
            Grid.SetRow(dayGrid, 0);
            Grid.SetRowSpan(dayGrid, totalSlots + 1);
            Grid.SetColumn(dayGrid, d + 1);
            tGrid.Children.Add(dayGrid);
        }

        // The now marker: a red dot in today's column and a line across all seven days
        var now = DateTime.Now;
        if (now.Date >= weekStart && now.Date <= weekEnd)
        {
            int    todayCol  = (((int)now.DayOfWeek + 6) % 7) + 1;
            double nowH      = now.Hour + now.Minute / 60.0;
            if (nowH >= _weekStartH && nowH < _weekEndH)
            {
                double offH    = nowH - _weekStartH;
                double slotDbl = offH * (60.0 / _slotMin);
                int    nowSlot = (int)slotDbl;
                double mTop    = (slotDbl - nowSlot) * _slotPx;

                // Dot on the left edge of today's column
                var dot = new WpfBorder
                {
                    Width             = 8,
                    Height            = 8,
                    CornerRadius      = new CornerRadius(4),
                    Background        = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = WpfHA.Left,
                    Margin            = new Thickness(-4, mTop - 4, 0, 0),
                    IsHitTestVisible  = false
                };
                Grid.SetRow(dot, nowSlot);
                Grid.SetColumn(dot, todayCol);
                tGrid.Children.Add(dot);

                // Line across all seven columns, the way Google Calendar does it
                var nowLine = new WpfBorder
                {
                    Height            = 2,
                    Background        = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin            = new Thickness(0, mTop - 1, 0, 0),
                    IsHitTestVisible  = false
                };
                Grid.SetRow(nowLine, nowSlot);
                Grid.SetColumn(nowLine, 1);          // start at the first day column
                Grid.SetColumnSpan(nowLine, 7);      // and reach across the whole week
                tGrid.Children.Add(nowLine);
            }
        }

        // ScrollViewer that jumps to the current time on its own
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = tGrid
        };
        // Always start at the top, 07:00
        scroll.Loaded += (_, _) => scroll.ScrollToTop();

        Grid.SetRow(scroll, 1);
        Grid.SetColumn(scroll, 0);
        WeekGrid.Children.Add(scroll);
    }

    private UIElement MakeWeekAllDayStrip(SchulnetzEvent ev)
    {
        var color = GetEventColor(ev);
        var strip = new WpfBorder
        {
            Background   = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(3),
            Margin       = new Thickness(2, 2, 2, 1),
            Padding      = new Thickness(4, 1, 4, 1),
            Cursor       = WpfCursors.Hand,
            Tag          = ev
        };
        strip.Child = new WpfTextBlock
        {
            Text         = ev.Summary,
            FontSize     = 9,
            FontWeight   = FontWeights.SemiBold,
            Foreground   = WpfBrushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        strip.MouseLeftButtonUp += EventPill_MouseUp;
        return strip;
    }

    // Returns WpfBorder so the caller can set margin and height on it directly
    private WpfBorder MakeWeekEventCard(SchulnetzEvent ev)
    {
        var  color = GetEventColor(ev);
        bool isSel = ev == _selectedEvent;

        var card = new WpfBorder
        {
            Background   = new SolidColorBrush(
                               Color.FromArgb(isSel ? (byte)220 : (byte)190,
                                              color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(4),
            Margin       = new Thickness(2, 1, 2, 1), // the caller overwrites this
            Padding      = new Thickness(5, 3, 5, 3),
            Cursor       = WpfCursors.Hand,
            Tag          = ev,
            ClipToBounds = true
        };

        var timeStr = $"{ev.Start.LocalDateTime:H:mm}–{ev.End.LocalDateTime:H:mm}";
        var inner   = new StackPanel();
        inner.Children.Add(new WpfTextBlock
        {
            Text       = timeStr,
            FontSize   = 8,
            Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            Margin     = new Thickness(0, 0, 0, 1)
        });
        var (title, rest) = ShortTitle(ev);
        card.ToolTip      = ev.Summary;
        inner.Children.Add(new WpfTextBlock
        {
            Text         = title,
            FontSize     = 10,
            FontWeight   = FontWeights.SemiBold,
            Foreground   = WpfBrushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        if (rest.Length > 0)
            inner.Children.Add(new WpfTextBlock
            {
                Text         = rest,
                FontSize     = 8.5,
                Foreground   = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        if (!string.IsNullOrWhiteSpace(ev.Location))
        {
            inner.Children.Add(new WpfTextBlock
            {
                Text       = "📍 " + ev.Location,
                FontSize   = 8,
                Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                Margin     = new Thickness(0, 2, 0, 0)
            });
        }

        var subject = CommentSubjectOf(ev);
        if (subject is not null && AppState.CommentCount(subject) > 0)
        {
            // Small speech bubble in the corner: there are notes on this subject
            var layered = new Grid();
            layered.Children.Add(inner);
            layered.Children.Add(new WpfTextBlock
            {
                Text                = "\uE8BD",
                FontFamily          = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize            = 9,
                Foreground          = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                HorizontalAlignment = WpfHA.Right,
                VerticalAlignment   = VerticalAlignment.Top,
                ToolTip             = $"Kommentare zu {subject}",
            });
            card.Child = layered;
        }
        else
        {
            card.Child = inner;
        }
        card.MouseLeftButtonUp += EventPill_MouseUp;
        return card;
    }

    // ══════════════════════════════════════════════════════════════════════
    // The side panel
    // ══════════════════════════════════════════════════════════════════════

    private void OpenPanel(string mode, int width = 340)
    {
        _panelMode                 = mode;
        DetailPanel.Visibility     = Visibility.Visible;
        DetailColumnDef.Width      = new GridLength(width);

        // Content sections
        DetailContent.Visibility   = mode == "Detail"   ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = mode == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        AddEventSection.Visibility = mode == "AddEvent" ? Visibility.Visible : Visibility.Collapsed;

        // Footer sections
        DetailFooter.Visibility        = mode == "Detail"   ? Visibility.Visible : Visibility.Collapsed;
        BtnSaveManualEvent.Visibility   = mode == "AddEvent" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClosePanel()
    {
        _panelMode             = "None";
        _selectedEvent         = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailColumnDef.Width  = new GridLength(0);
    }

    // ══════════════════════════════════════════════════════════════════════
    // DETAIL PANEL
    // ══════════════════════════════════════════════════════════════════════
    private void ShowDetailPanel(SchulnetzEvent ev)
    {
        var  color      = GetEventColor(ev);
        var  colorKey   = GetColorKey(ev);
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
        bool isLektion  = ev.Type == SchulnetzEventType.Lektion;
        bool isManual   = EventKeys.IsManual(ev.Key);

        // Panel heading
        TxtPanelTitle.Text       = isPruefung ? "⚠  PRÜFUNG" : isLektion ? "📘  STUNDE" : "📌  TERMIN";
        TxtPanelTitle.Foreground = new SolidColorBrush(color);
        BtnDeleteFromApp.Tag     = ev;

        DetailContent.Children.Clear();

        // Colour stripe
        DetailContent.Children.Add(new WpfBorder
        {
            Height              = 4,
            CornerRadius        = new CornerRadius(2),
            Background          = new SolidColorBrush(color),
            Margin              = new Thickness(0, 0, 0, 14),
            HorizontalAlignment = WpfHA.Left,
            Width               = 48
        });

        // Title
        DetailContent.Children.Add(new WpfTextBlock
        {
            Text         = ev.Summary,
            FontSize     = 16,
            FontWeight   = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 16)
        });

        // Date
        DetailContent.Children.Add(MakeDetailRow("📅",
            ev.Start.LocalDateTime.ToString("dddd, d. MMMM yyyy", _deCH)));

        // Time
        var timeStr = ev.IsAllDay ? "Ganztägig"
            : $"{ev.Start.LocalDateTime:HH:mm} – {ev.End.LocalDateTime:HH:mm} Uhr";
        DetailContent.Children.Add(MakeDetailRow("🕐", timeStr));

        // Room
        if (!string.IsNullOrWhiteSpace(ev.Location))
            DetailContent.Children.Add(MakeDetailRow("📍", ev.Location!));

        // What kind of entry this is
        var typeHint = isPruefung ? "Als Prüfung klassifiziert."
                     : isLektion  ? $"Fach: {colorKey}"
                     : isManual   ? "Manuell erstellter Eintrag."
                     :              "Als Schultermin klassifiziert.";
        DetailContent.Children.Add(new WpfTextBlock
        {
            Text         = typeHint,
            FontSize     = 11,
            Opacity      = 0.42,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 10, 0, 20)
        });

        // ── Notes on the subject ──
        if (CommentSubjectOf(ev) is { } subject)
            DetailContent.Children.Add(BuildCommentSection(ev, subject));

        // ── Colour ──
        // When another entry is selected, pick the level based on whether it already has
        // its own colour, and close the mixer.
        if (_colorScopeEventKey != ev.Key)
        {
            _colorScopeEventKey = ev.Key;
            _colorScopeSingle   = AppState.HasOwnEventColor(ev.Key);
            _mixerOpen          = false;
        }
        DetailContent.Children.Add(BuildColorSection(ev, colorKey, isLektion, isPruefung));

        // Delete, for hand-made entries only
        if (isManual)
        {
            DetailContent.Children.Add(new WpfTextBlock
            {
                Text     = "Manuellen Eintrag löschen:",
                FontSize = 11,
                Opacity  = 0.60,
                Margin   = new Thickness(0, 16, 0, 6)
            });
            var btnDelete = new WpfButton
            {
                Content             = "🗑  Eintrag löschen",
                Style               = (Style)FindResource("SecondaryButton"),
                HorizontalAlignment = WpfHA.Left,
                Padding             = new Thickness(12, 5, 12, 5),
                Tag                 = ev
            };
            btnDelete.Click += BtnDeleteManual_Click;
            DetailContent.Children.Add(btnDelete);
        }

        OpenPanel("Detail");
    }

    // ── Subject comments inside the detail panel ─────────────────────────────

    /// <summary>Draft text in the input box; survives the panel being rebuilt.</summary>
    private string _commentDraft = "";

    /// <summary>The subject <see cref="_commentDraft"/> belongs to.</summary>
    private string? _commentDraftSubject;

    /// <summary>The comment being edited right now, and what has been typed so far.</summary>
    private Guid?  _editingCommentId;
    private string _editingText = "";

    private UIElement BuildCommentSection(SchulnetzEvent ev, string subject)
    {
        if (_commentDraftSubject != subject)
        {
            _commentDraftSubject = subject;
            _commentDraft        = "";
            _editingCommentId    = null;
        }

        var comments = AppState.CommentsFor(subject);
        var section  = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };

        // Header: title and count
        var head = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new WpfTextBlock
        {
            Text       = $"KOMMENTARE ZU {subject}",
            FontSize   = 11,
            FontWeight = FontWeights.Bold,
            Opacity    = 0.55,
        });
        if (comments.Count > 0)
            head.Children.Add(new WpfBorder
            {
                CornerRadius      = new CornerRadius(8),
                Padding           = new Thickness(6, 0, 6, 1),
                Margin            = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background        = new SolidColorBrush(Color.FromArgb(0x33, _accentColor.R, _accentColor.G, _accentColor.B)),
                Child = new WpfTextBlock { Text = comments.Count.ToString(), FontSize = 10.5, FontWeight = FontWeights.SemiBold },
            });
        section.Children.Add(head);

        section.Children.Add(new WpfTextBlock
        {
            Text         = $"Gilt für alle {subject}-Stunden und -Prüfungen.",
            FontSize     = 11.5,
            Opacity      = 0.5,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 10),
        });

        // Input box
        var input = new TextBox
        {
            Text                          = _commentDraft,
            AcceptsReturn                 = true,
            TextWrapping                  = TextWrapping.Wrap,
            MinHeight                     = 64,
            MaxHeight                     = 160,
            MaxLength                     = SubjectComment.MaxLength,
            FontSize                      = 13,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
        };
        ControlHelper.SetPlaceholderText(input, $"Notiz zu {subject}, z. B. Material, Tipps, Abmachungen");

        var add = new WpfButton
        {
            Content             = "Hinzufügen",
            Style               = (Style)FindResource("PrimaryButton"),
            Padding             = new Thickness(14, 6, 14, 6),
            FontSize            = 12.5,
            HorizontalAlignment = WpfHA.Left,
            IsEnabled           = _commentDraft.Trim().Length > 0,
        };

        void Submit()
        {
            if (input.Text.Trim().Length == 0) return;
            AppState.AddSubjectComment(subject, input.Text);
            _commentDraft = "";
            ShowDetailPanel(ev);
        }

        input.TextChanged += (_, _) =>
        {
            _commentDraft  = input.Text;
            add.IsEnabled  = input.Text.Trim().Length > 0;
        };
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Submit();
                e.Handled = true;
            }
        };
        add.Click += (_, _) => Submit();

        section.Children.Add(input);

        var actions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(add);
        actions.Children.Add(new WpfTextBlock
        {
            Text                = "Strg+Enter",
            FontSize            = 11,
            Opacity             = 0.4,
            HorizontalAlignment = WpfHA.Right,
            VerticalAlignment   = VerticalAlignment.Center,
        });
        section.Children.Add(actions);

        // The notes themselves
        foreach (var comment in comments)
            section.Children.Add(CommentCard(ev, comment));

        return section;
    }

    private UIElement CommentCard(SchulnetzEvent ev, SubjectComment comment)
    {
        var card = new WpfBorder
        {
            Margin          = new Thickness(0, 10, 0, 0),
            Padding         = new Thickness(12, 10, 10, 10),
            CornerRadius    = new CornerRadius(10),
            Background      = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
        };
        var body = new StackPanel();
        card.Child = body;

        if (_editingCommentId == comment.Id)
        {
            var editor = new TextBox
            {
                Text                        = _editingText,
                AcceptsReturn               = true,
                TextWrapping                = TextWrapping.Wrap,
                MinHeight                   = 60,
                MaxHeight                   = 200,
                MaxLength                   = SubjectComment.MaxLength,
                FontSize                    = 13,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            editor.TextChanged += (_, _) => _editingText = editor.Text;

            void Save()
            {
                AppState.UpdateSubjectComment(comment.Id, editor.Text);
                _editingCommentId = null;
                ShowDetailPanel(ev);
            }
            editor.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Save(); e.Handled = true; }
                else if (e.Key == Key.Escape) { _editingCommentId = null; ShowDetailPanel(ev); e.Handled = true; }
            };
            body.Children.Add(editor);

            var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var save = new WpfButton
            {
                Content = "Speichern",
                Style   = (Style)FindResource("PrimaryButton"),
                Padding = new Thickness(12, 5, 12, 5),
                FontSize = 12,
                Margin  = new Thickness(0, 0, 8, 0),
            };
            save.Click += (_, _) => Save();
            var cancel = new WpfButton
            {
                Content  = "Abbrechen",
                Style    = (Style)FindResource("SecondaryButton"),
                Padding  = new Thickness(12, 5, 12, 5),
                FontSize = 12,
            };
            cancel.Click += (_, _) => { _editingCommentId = null; ShowDetailPanel(ev); };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            body.Children.Add(buttons);

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                editor.Focus();
                editor.CaretIndex = editor.Text.Length;
            });
            return card;
        }

        body.Children.Add(new WpfTextBlock
        {
            Text         = comment.Text,
            FontSize     = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight   = 19,
        });

        var meta = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        var stamp = comment.CreatedAt.LocalDateTime.ToString("d. MMM yyyy, HH:mm", _deCH)
                  + (comment.EditedAt is null ? "" : " · bearbeitet");
        meta.Children.Add(new WpfTextBlock
        {
            Text              = stamp,
            FontSize          = 11,
            Opacity           = 0.45,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var tools = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHA.Right };
        tools.Children.Add(CommentTool("\uE70F", "Bearbeiten", () =>
        {
            _editingCommentId = comment.Id;
            _editingText      = comment.Text;
            ShowDetailPanel(ev);
        }));
        tools.Children.Add(CommentTool("\uE74D", "Löschen", () =>
        {
            if (!Confirm("Diesen Kommentar löschen?")) return;
            AppState.RemoveSubjectComment(comment.Id);
            ShowDetailPanel(ev);
        }));
        meta.Children.Add(tools);
        body.Children.Add(meta);

        return card;
    }

    private static WpfBorder CommentTool(string glyph, string tooltip, Action onClick)
    {
        var tool = new WpfBorder
        {
            Width        = 26,
            Height       = 24,
            CornerRadius = new CornerRadius(6),
            Background   = WpfBrushes.Transparent,
            Cursor       = WpfCursors.Hand,
            ToolTip      = tooltip,
            Margin       = new Thickness(2, 0, 0, 0),
            Child = new WpfTextBlock
            {
                Text                = glyph,
                FontFamily          = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize            = 12,
                Opacity             = 0.65,
                HorizontalAlignment = WpfHA.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            },
        };
        tool.MouseEnter += (_, _) => tool.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80));
        tool.MouseLeave += (_, _) => tool.Background = WpfBrushes.Transparent;
        tool.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        return tool;
    }

    // ── Picking a colour in the detail panel ─────────────────────────────────

    /// <summary>True colours this one entry, false colours the whole subject or category.</summary>
    private bool _colorScopeSingle;

    /// <summary>The entry <see cref="_colorScopeSingle"/> was last decided for.</summary>
    private string? _colorScopeEventKey;

    private bool _mixerOpen;

    private UIElement BuildColorSection(SchulnetzEvent ev, string colorKey, bool isLektion, bool isPruefung)
    {
        var section  = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        bool hasOwn  = AppState.HasOwnEventColor(ev.Key);

        var groupLabel  = isLektion ? $"Alle {colorKey}-Stunden"
                        : isPruefung ? "Alle Prüfungen"
                        :              "Alle Termine";
        var singleLabel = isLektion ? "Nur diese Stunde" : "Nur dieser Eintrag";

        section.Children.Add(new WpfTextBlock
        {
            Text       = "FARBE",
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Opacity    = 0.6,
            Margin     = new Thickness(0, 0, 0, 8),
        });

        // ── Switch: which level gets coloured ──
        var scope = new StackPanel { Orientation = WpfOrientation.Horizontal };
        scope.Children.Add(ScopeButton(singleLabel, _colorScopeSingle, () =>
        {
            _colorScopeSingle = true;
            ShowDetailPanel(ev);
        }));
        scope.Children.Add(ScopeButton(groupLabel, !_colorScopeSingle, () =>
        {
            _colorScopeSingle = false;
            ShowDetailPanel(ev);
        }));
        section.Children.Add(new WpfBorder
        {
            Child               = scope,
            Padding             = new Thickness(3),
            CornerRadius        = new CornerRadius(8),
            Background          = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
            HorizontalAlignment = WpfHA.Left,
            Margin              = new Thickness(0, 0, 0, 8),
        });

        // Spell out what the choice does. It matters most when an individual colour hides
        // the subject colour, because then changing the subject shows nothing here.
        // Worded by hand rather than via ToLower(), so the subject code stays upper case.
        var groupPhrase = isLektion  ? $"alle {colorKey}-Stunden"
                        : isPruefung ? "alle Prüfungen"
                        :              "alle Termine";
        var hint = _colorScopeSingle
            ? (hasOwn ? "Dieser Eintrag hat eine eigene Farbe." : $"Färbt nur diesen Eintrag, nicht {groupPhrase}.")
            : (hasOwn ? "Dieser Eintrag hat eine eigene Farbe und behält sie."
                      : $"Färbt {groupPhrase}.");
        section.Children.Add(new WpfTextBlock
        {
            Text         = hint,
            FontSize     = 11,
            Opacity      = 0.5,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 10),
        });

        // ── Palette ──
        var currentHex = _colorScopeSingle
            ? AppState.GetEventColor(ev.Key, colorKey)
            : AppState.GetEventColor(colorKey);
        bool inPalette = _palette.Any(p => string.Equals(p.Hex, currentHex, StringComparison.OrdinalIgnoreCase));

        // Seven per row, which keeps the colour families from ColorPalette together
        var paletteWrap = new WpfWrapPanel
        {
            Orientation         = WpfOrientation.Horizontal,
            MaxWidth            = 7 * 30,
            HorizontalAlignment = WpfHA.Left,
        };
        foreach (var (hex, name) in _palette)
        {
            bool active = string.Equals(hex, currentHex, StringComparison.OrdinalIgnoreCase);
            var dot = ColorDot(new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)),
                               name, active, null);
            dot.MouseLeftButtonUp += (_, e) => { ApplyColor(ev, colorKey, hex); e.Handled = true; };
            paletteWrap.Children.Add(dot);
        }

        // Custom colour: the rainbow circle, marked when the current colour was mixed
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var (stop, c) in new[] { (0.0, "#FF0000"), (0.33, "#FFD400"), (0.55, "#00D26A"), (0.78, "#0A84FF"), (1.0, "#BF5AF2") })
            rainbow.GradientStops.Add(new GradientStop((Color)System.Windows.Media.ColorConverter.ConvertFromString(c), stop));

        section.Children.Add(paletteWrap);

        // On its own labelled row: a lone circle at the end of the palette is far too
        // easy to miss.
        var mixDot = ColorDot(rainbow, "Eigene Farbe mischen", _mixerOpen || !inPalette, "+");
        mixDot.Margin = new Thickness(0, 0, 10, 0);
        var customRow = new StackPanel { Orientation = WpfOrientation.Horizontal };
        customRow.Children.Add(mixDot);
        customRow.Children.Add(new WpfTextBlock
        {
            Text              = _mixerOpen ? "Mischer schliessen" : "Eigene Farbe mischen…",
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var custom = new WpfBorder
        {
            Child               = customRow,
            Background          = WpfBrushes.Transparent,   // makes the whole row clickable
            Cursor              = WpfCursors.Hand,
            HorizontalAlignment = WpfHA.Left,
            Margin              = new Thickness(0, 4, 0, 4),
        };
        custom.MouseLeftButtonUp += (_, e) =>
        {
            _mixerOpen = !_mixerOpen;
            ShowDetailPanel(ev);
            e.Handled = true;
        };
        section.Children.Add(custom);

        // ── Mixer ──
        if (_mixerOpen)
        {
            var mixer = new ColorMixer { Margin = new Thickness(0, 8, 0, 10) };
            if (RgbColor.TryParseHex(currentHex, out var start))
                mixer.SelectedColor = start;
            section.Children.Add(mixer);

            var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var apply = new WpfButton
            {
                Content = "Übernehmen",
                Style   = (Style)FindResource("PrimaryButton"),
                Padding = new Thickness(14, 6, 14, 6),
                Margin  = new Thickness(0, 0, 8, 0),
            };
            apply.Click += (_, _) => ApplyColor(ev, colorKey, mixer.SelectedColor.ToHex());
            var cancel = new WpfButton
            {
                Content = "Abbrechen",
                Style   = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(14, 6, 14, 6),
            };
            cancel.Click += (_, _) => { _mixerOpen = false; ShowDetailPanel(ev); };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            section.Children.Add(buttons);
        }

        // ── Remove the individual colour ──
        if (_colorScopeSingle && hasOwn)
        {
            var reset = new WpfButton
            {
                Content             = isLektion ? $"Eigene Farbe entfernen (zurück zu {colorKey})"
                                                : "Eigene Farbe entfernen",
                Style               = (Style)FindResource("SecondaryButton"),
                HorizontalAlignment = WpfHA.Left,
                Padding             = new Thickness(12, 5, 12, 5),
                Margin              = new Thickness(0, 6, 0, 0),
            };
            reset.Click += (_, _) =>
            {
                AppState.ClearOwnEventColor(ev.Key);
                ShowDetailPanel(ev);
            };
            section.Children.Add(reset);
        }

        return section;
    }

    /// <summary>Saves the colour at the chosen level and rebuilds the panel.</summary>
    private void ApplyColor(SchulnetzEvent ev, string colorKey, string hex)
    {
        _mixerOpen = false;

        if (_colorScopeSingle)
            AppState.SetOwnEventColor(ev.Key, hex);
        else
            AppState.SetCategoryColor(colorKey, hex);   // fires Notify, which refreshes us

        ShowDetailPanel(ev);
    }

    private WpfBorder ColorDot(Brush fill, string tooltip, bool active, string? glyph) => new()
    {
        Width           = 24,
        Height          = 24,
        CornerRadius    = new CornerRadius(12),
        Margin          = new Thickness(0, 0, 6, 6),
        Cursor          = WpfCursors.Hand,
        Background      = fill,
        ToolTip         = tooltip,
        BorderThickness = active ? new Thickness(2.5) : new Thickness(0),
        BorderBrush     = active ? WpfBrushes.White : null,
        Child           = glyph is null ? null : new WpfTextBlock
        {
            Text                = glyph,
            Foreground          = WpfBrushes.White,
            FontWeight          = FontWeights.Bold,
            FontSize            = 15,
            HorizontalAlignment = WpfHA.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(0, -2, 0, 0),
        },
    };

    private WpfBorder ScopeButton(string label, bool active, Action onClick)
    {
        var text = new WpfTextBlock { Text = label, FontSize = 12 };
        if (active)
            text.Foreground = WpfBrushes.White;
        else
            text.SetResourceReference(WpfTextBlock.ForegroundProperty, "SystemControlForegroundBaseHighBrush");

        var button = new WpfBorder
        {
            Child        = text,
            Padding      = new Thickness(10, 5, 10, 6),
            CornerRadius = new CornerRadius(6),
            Cursor       = WpfCursors.Hand,
            Background   = active ? (Brush)FindResource("AccentBrush") : WpfBrushes.Transparent,
        };
        button.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        return button;
    }

    // ══════════════════════════════════════════════════════════════════════
    // SETTINGS PANEL
    // ══════════════════════════════════════════════════════════════════════
    private void ShowSettingsPanel()
    {
        TxtPanelTitle.Text       = "⚙  KALENDER-EINSTELLUNGEN";
        TxtPanelTitle.SetResourceReference(WpfTextBlock.ForegroundProperty, "SystemControlForegroundBaseHighBrush");
        _selectedEvent           = null;

        SettingsContent.Children.Clear();

        // ─ Category colours ─────────────────────────────────────────────
        SettingsContent.Children.Add(new WpfTextBlock
        {
            Text       = "Kategoriefarben",
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
            Margin     = new Thickness(0, 0, 0, 12)
        });

        foreach (var (key, label) in new[] { ("Pruefung", "Prüfungen"), ("Termin", "Termine"), ("Lektion", "Stunden (Standard)") })
        {
            SettingsContent.Children.Add(MakeCategoryColorRow(key, label));
        }

        // Subjects that have a colour of their own
        var customKeys = AppState.CategoryColors.Keys
            .Where(k => k != "Pruefung" && k != "Termin" && k != "Lektion")
            .OrderBy(k => k)
            .ToList();

        if (customKeys.Count > 0)
        {
            SettingsContent.Children.Add(new WpfTextBlock
            {
                Text       = "Individuelle Fachfarben",
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold,
                Opacity    = 0.65,
                Margin     = new Thickness(0, 16, 0, 10)
            });
            foreach (var key in customKeys)
                SettingsContent.Children.Add(MakeCategoryColorRow(key, key));
        }

        // Divider
        SettingsContent.Children.Add(new Separator
        {
            Margin = new Thickness(0, 20, 0, 16),
            Style  = (Style)FindResource("Divider")
        });

        // ─ Calendar actions ──────────────────────────────────────────────
        SettingsContent.Children.Add(new WpfTextBlock
        {
            Text       = "Kalender verwalten",
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
            Margin     = new Thickness(0, 0, 0, 12)
        });

        AddSettingsButton("Ausgeblendete Einträge zurücksetzen",
            "Macht alle ausgeblendeten Feed-Einträge wieder sichtbar.", false,
            () => { AppState.ClearSuppressed(); BuildSettingsContent(); });

        AddSettingsButton("Manuelle Einträge löschen",
            "Löscht alle manuell hinzugefügten Termine.", false,
            () =>
            {
                if (Confirm("Alle manuellen Einträge wirklich löschen?"))
                { AppState.ClearManualEvents(); BuildSettingsContent(); }
            });

        AddSettingsButton("Alle Farben zurücksetzen",
            "Setzt alle Kategoriefarben auf die Standardfarben zurück.", false,
            () => { AppState.ResetCategoryColors(); BuildSettingsContent(); });

        AddSettingsButton("⚠  Alles zurücksetzen",
            "Löscht alle Farben, ausgeblendeten Einträge und manuelle Events.", true,
            () =>
            {
                if (Confirm("Wirklich alles zurücksetzen? Farben, ausgeblendete Einträge und manuelle Events werden gelöscht."))
                { AppState.ResetAll(); BuildSettingsContent(); }
            });

        OpenPanel("Settings", 360);
    }

    private void BuildSettingsContent()
    {
        // Rebuild the panel after a reset
        ShowSettingsPanel();
    }

    private UIElement MakeCategoryColorRow(string key, string label)
    {
        var hex = AppState.GetEventColor(key);

        // Label on top, palette below: side by side the palette gets clipped
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        container.Children.Add(new WpfTextBlock
        {
            Text       = label,
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity    = 0.85,
            Margin     = new Thickness(0, 0, 0, 6)
        });

        var palette = new WpfWrapPanel { Orientation = WpfOrientation.Horizontal };
        foreach (var (pHex, pName) in _palette)
        {
            bool isActive = string.Equals(pHex, hex, StringComparison.OrdinalIgnoreCase);
            var dot = new WpfBorder
            {
                Width        = isActive ? 20 : 22,
                Height       = isActive ? 20 : 22,
                CornerRadius = new CornerRadius(11),
                Margin       = new Thickness(0, 0, 5, 5),
                Cursor       = WpfCursors.Hand,
                Background   = new SolidColorBrush(
                                   (Color)System.Windows.Media.ColorConverter.ConvertFromString(pHex)),
                ToolTip      = pName,
                Tag          = (key, pHex),
                BorderThickness = isActive ? new Thickness(2) : new Thickness(0),
                BorderBrush     = isActive ? WpfBrushes.White : null
            };
            dot.MouseLeftButtonUp += ColorDot_Click;
            palette.Children.Add(dot);
        }
        container.Children.Add(palette);
        return container;
    }

    private void AddSettingsButton(string label, string hint, bool isDanger, Action onClick)
    {
        var btn = new WpfButton
        {
            Content             = label,
            Style               = (Style)FindResource("SecondaryButton"),
            HorizontalAlignment = WpfHA.Stretch,
            Padding             = new Thickness(12, 7, 12, 7),
            Margin              = new Thickness(0, 0, 0, 6),
            Tag                 = onClick
        };
        if (isDanger)
            btn.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
        btn.Click += (_, _) => { if (btn.Tag is Action a) a(); };
        SettingsContent.Children.Add(btn);

        SettingsContent.Children.Add(new WpfTextBlock
        {
            Text     = hint,
            FontSize = 10,
            Opacity  = 0.45,
            Margin   = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    // ADD-EVENT PANEL
    // ══════════════════════════════════════════════════════════════════════
    private void ShowAddEventPanel()
    {
        TxtPanelTitle.Text       = "➕  NEUER EINTRAG";
        TxtPanelTitle.SetResourceReference(WpfTextBlock.ForegroundProperty, "SystemControlForegroundBaseHighBrush");
        _selectedEvent           = null;

        // Clear the form
        TxtNewTitle.Text      = "";
        DpNewDate.SelectedDate = _selectedDate ?? DateTime.Today;
        ChkNewAllDay.IsChecked = false;
        TxtStartTime.Text     = "08:00";
        TxtEndTime.Text       = "09:00";
        CmbNewType.SelectedIndex = 0;
        TxtNewLocation.Text   = "";
        TimePickers.Visibility = Visibility.Visible;

        OpenPanel("AddEvent", 320);
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void ChkNewAllDay_Changed(object sender, RoutedEventArgs e)
    {
        TimePickers.Visibility = ChkNewAllDay.IsChecked == true
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnSaveManualEvent_Click(object sender, RoutedEventArgs e)
    {
        // Validation
        var title = TxtNewTitle.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            MessageBox.Show("Bitte gib einen Titel ein.", "Fehlende Angabe",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DpNewDate.SelectedDate is not DateTime date)
        {
            MessageBox.Show("Bitte wähle ein Datum.", "Fehlende Angabe",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isAllDay = ChkNewAllDay.IsChecked == true;
        DateTimeOffset start, end;

        if (isAllDay)
        {
            start = new DateTimeOffset(date, TimeSpan.Zero);
            end   = start.AddDays(1);
        }
        else
        {
            if (!TryParseTime(TxtStartTime.Text, out var startTs) ||
                !TryParseTime(TxtEndTime.Text, out var endTs))
            {
                MessageBox.Show("Zeit im Format HH:MM eingeben (z.B. 08:30).", "Ungültige Zeit",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            start = new DateTimeOffset(date + startTs, TimeZoneInfo.Local.GetUtcOffset(date + startTs));
            end   = new DateTimeOffset(date + endTs, TimeZoneInfo.Local.GetUtcOffset(date + endTs));
            if (end <= start) end = start.AddHours(1);
        }

        var typeKey = (CmbNewType.SelectedItem as ComboBoxItem)?.Tag as string ?? "Termin";
        var location = string.IsNullOrWhiteSpace(TxtNewLocation.Text) ? null : TxtNewLocation.Text.Trim();

        var manualEvent = new ManualEventData(
            Id:       Guid.NewGuid(),
            Title:    title,
            Start:    start,
            End:      end,
            IsAllDay: isAllDay,
            Location: location,
            TypeKey:  typeKey);

        AppState.AddManualEvent(manualEvent); // fires Notify, which refreshes us
        ClosePanel();
    }

    private static bool TryParseTime(string text, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        result = new TimeSpan(h, m, 0);
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Colour clicks
    // ══════════════════════════════════════════════════════════════════════
    private void ColorDot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfBorder dot) return;
        if (dot.Tag is not (string key, string hex)) return;

        AppState.SetCategoryColor(key, hex); // fires Notify, which refreshes us

        // Rebuild the panel
        if (_panelMode == "Settings")
            ShowSettingsPanel();
        else if (_selectedEvent != null)
            ShowDetailPanel(_selectedEvent);

        e.Handled = true;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Navigation, filters, buttons
    // ══════════════════════════════════════════════════════════════════════

    private void EventPill_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfBorder pill && pill.Tag is SchulnetzEvent ev)
        {
            _selectedEvent = ev;
            _selectedDate  = ev.Start.Date;
            ShowDetailPanel(ev);
            Refresh();
            e.Handled = true;
        }
    }

    private void DayCell_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfBorder border || border.Tag is not DateTime date) return;

        if (date.Year != _year || date.Month != _month)
        {
            _year = date.Year; _month = date.Month;
        }

        if (_selectedDate == date && _selectedEvent == null)
            _selectedDate = null;
        else
        {
            _selectedDate  = date;
            _selectedEvent = null;
            if (_panelMode == "Detail") ClosePanel();
        }

        Refresh();
    }

    private void BtnClosePanel_Click(object sender, RoutedEventArgs e)
    {
        _selectedEvent = null;
        ClosePanel();
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
        AppState.SuppressEvent(ev.Key);
        _selectedEvent = null;
        ClosePanel();
    }

    private void BtnDeleteManual_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not SchulnetzEvent ev) return;
        if (!EventKeys.IsManual(ev.Key)) return;

        if (!Confirm($"«{ev.Summary}» wirklich löschen?")) return;

        var id = Guid.ParseExact(ev.Key[EventKeys.ManualPrefix.Length..], "N");
        AppState.RemoveManualEvent(id);
        _selectedEvent = null;
        ClosePanel();
    }

    private void BtnCalSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_panelMode == "Settings") { ClosePanel(); Refresh(); return; }
        _selectedEvent = null;
        ShowSettingsPanel();
        Refresh();
    }

    private void BtnAddEvent_Click(object sender, RoutedEventArgs e)
    {
        if (_panelMode == "AddEvent") { ClosePanel(); Refresh(); return; }
        _selectedEvent = null;
        ShowAddEventPanel();
        Refresh();
    }

    // ── Moving through the months and weeks ─────────────────────────────────

    private void BtnPrevMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == "Week")
        {
            var anchor = (_selectedDate ?? DateTime.Today).AddDays(-7);
            int d = ((int)anchor.DayOfWeek + 6) % 7;
            _selectedDate = anchor.AddDays(-d);
            _year = _selectedDate.Value.Year; _month = _selectedDate.Value.Month;
        }
        else
        {
            var d = new DateTime(_year, _month, 1).AddMonths(-1);
            _year = d.Year; _month = d.Month;
            _selectedDate = null;
        }
        _selectedEvent = null;
        if (_panelMode == "Detail") ClosePanel();
        Refresh();
    }

    private void BtnNextMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == "Week")
        {
            var anchor = (_selectedDate ?? DateTime.Today).AddDays(7);
            int d = ((int)anchor.DayOfWeek + 6) % 7;
            _selectedDate = anchor.AddDays(-d);
            _year = _selectedDate.Value.Year; _month = _selectedDate.Value.Month;
        }
        else
        {
            var d = new DateTime(_year, _month, 1).AddMonths(1);
            _year = d.Year; _month = d.Month;
            _selectedDate = null;
        }
        _selectedEvent = null;
        if (_panelMode == "Detail") ClosePanel();
        Refresh();
    }

    private void BtnToday_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        _year = today.Year; _month = today.Month;
        _selectedDate  = today;
        _selectedEvent = null;
        if (_panelMode == "Detail") ClosePanel();
        Refresh();
    }

    // ── View switch: month or week ───────────────────────────────────────────

    private void BtnViewMonth_Click(object sender, RoutedEventArgs e)
    {
        // Checked already fires during InitializeComponent()
        if (!IsLoaded) return;
        _viewMode = "Month";
        Refresh();
    }

    private void BtnViewWeek_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _viewMode = "Week";
        if (!_selectedDate.HasValue) _selectedDate = DateTime.Today;
        Refresh();
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not FrameworkElement chip) return;
        _filter        = chip.Tag as string ?? "All";
        _selectedEvent = null;
        if (_panelMode == "Detail") ClosePanel();

        Refresh();
    }

    // ── Small helpers ────────────────────────────────────────────────────────

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
            Opacity           = 0.88,
            VerticalAlignment = VerticalAlignment.Top
        });
        return row;
    }

    private static bool Confirm(string message)
        => MessageBox.Show(message, "Bestätigung",
               MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    // ══════════════════════════════════════════════════════════════════════
    // Laying out overlapping events, the way Google Calendar does it
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gives every timed event a column within its day. Events that overlap share the
    /// available width evenly; an event with nothing beside it spans the lot
    /// (span = maxCols), so a lonely lesson does not end up as a thin strip.
    /// </summary>
    private static Dictionary<SchulnetzEvent, (int Col, int Span, int TotalCols)>
        AssignEventColumns(List<SchulnetzEvent> events)
    {
        if (events.Count == 0)
            return new Dictionary<SchulnetzEvent, (int, int, int)>();

        // Greedy: put each event in the earliest column that is free
        var sorted   = events.OrderBy(e => e.Start).ToList();
        var colEnds  = new List<DateTimeOffset>(); // when each column last became free
        var colIndex = new Dictionary<SchulnetzEvent, int>();

        foreach (var ev in sorted)
        {
            int col = -1;
            for (int c = 0; c < colEnds.Count; c++)
            {
                if (colEnds[c] <= ev.Start) { col = c; colEnds[c] = ev.End; break; }
            }
            if (col < 0) { col = colEnds.Count; colEnds.Add(ev.End); }
            colIndex[ev] = col;
        }

        int maxCols = colEnds.Count; // how many events overlap at the busiest moment

        var result = new Dictionary<SchulnetzEvent, (int, int, int)>();
        foreach (var ev in events)
        {
            int col = colIndex[ev];

            // Is anything else running while this event runs?
            bool hasConcurrent = events.Any(other =>
                !ReferenceEquals(other, ev) &&
                ev.Start  < other.End &&
                other.Start < ev.End);

            // An event on its own fills the whole day column
            int span = hasConcurrent ? 1 : maxCols;
            result[ev] = (col, span, maxCols);
        }

        return result;
    }
}
