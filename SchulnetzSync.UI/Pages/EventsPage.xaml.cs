using System.Globalization;
using System.Text.RegularExpressions;
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
using WpfWrapPanel   = System.Windows.Controls.WrapPanel;

namespace SchulnetzSync.UI.Pages;

public partial class EventsPage : WpfPage
{
    // ── Zustand ─────────────────────────────────────────────────────────────
    private int       _year;
    private int       _month;
    private DateTime? _selectedDate;
    private SchulnetzEvent? _selectedEvent;
    private string    _filter    = "All";
    private string    _viewMode  = "Month";    // "Month" | "Week"
    private string    _panelMode = "None";     // "None" | "Detail" | "Settings" | "AddEvent"

    // Zeit-Raster Konstanten
    private const int    _weekStartH = 7;      // 07:00
    private const int    _weekEndH   = 22;     // 22:00
    private const int    _slotMin    = 15;     // Minuten pro Zeile
    private const double _slotPx     = 15.0;  // Pixel pro Zeile (1h = 60px)
    private const double _gutterW    = 48.0;  // Breite der Zeit-Spalte

    private static readonly CultureInfo _deCH = CultureInfo.GetCultureInfo("de-CH");

    // Standardfarben (über CategoryColors überschreibbar)
    private static readonly Color _pruefungColor = Color.FromRgb(0xDC, 0x26, 0x26); // Rot
    private static readonly Color _terminColor   = Color.FromRgb(0xD9, 0x77, 0x06); // Amber/Gelb
    private static readonly Color _lektionColor  = Color.FromRgb(0x25, 0x63, 0xEB); // Blau
    private static readonly Color _accentColor   = Color.FromRgb(0x5C, 0x6E, 0xF7);

    // 14 vordefinierte Farben für den Color-Picker
    private static readonly (string Hex, string Name)[] _palette =
    {
        ("#DC2626", "Rot"),
        ("#EA580C", "Orange"),
        ("#D97706", "Amber"),
        ("#CA8A04", "Gelb"),
        ("#65A30D", "Limette"),
        ("#16A34A", "Grün"),
        ("#0D9488", "Türkis"),
        ("#0EA5E9", "Himmelblau"),
        ("#2563EB", "Blau"),
        ("#7C3AED", "Violett"),
        ("#A21CAF", "Lila"),
        ("#DB2777", "Pink"),
        ("#6B7280", "Grau"),
        ("#1E293B", "Dunkel"),
    };

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

    // ── Farb-Helpers ─────────────────────────────────────────────────────────

    /// <summary>Leitet den Farb-Schlüssel für ein Event ab.</summary>
    private static string GetColorKey(SchulnetzEvent ev)
    {
        if (ev.Key.StartsWith("MANUAL_", StringComparison.Ordinal))
            return ev.Type == SchulnetzEventType.Pruefung ? "Pruefung" : "Termin";
        return ev.Type switch
        {
            SchulnetzEventType.Pruefung => "Pruefung",
            SchulnetzEventType.Termin   => "Termin",
            _                           => ExtractSubjectCode(ev.Summary)
        };
    }

    /// <summary>Extrahiert das Fachkürzel aus einer Lektions-Summary (z.B. "TEU" aus "9:30 TEU_I26A").</summary>
    private static string ExtractSubjectCode(string summary)
    {
        var s   = Regex.Replace(summary, @"^\d{1,2}:\d{2}\s+", ""); // Zeitpräfix entfernen
        var idx = s.IndexOf('_');
        var code = idx > 0 ? s[..idx] : s[..Math.Min(s.Length, 6)];
        return code.ToUpperInvariant();
    }

    /// <summary>Gibt die effektive Farbe für ein Event zurück (custom → default).</summary>
    private Color GetEventColor(SchulnetzEvent ev)
    {
        var key = GetColorKey(ev);
        var hex = AppState.GetEventColor(key);
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

    /// <summary>Gibt die Farbe für einen Schlüssel direkt zurück.</summary>
    private static Color GetColorForKey(string key)
    {
        var hex = AppState.GetEventColor(key);
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch { return key == "Pruefung" ? Color.FromRgb(0xDC, 0x26, 0x26)
                     : key == "Termin"   ? Color.FromRgb(0xD9, 0x77, 0x06)
                     :                     Color.FromRgb(0x25, 0x63, 0xEB); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Haupt-Refresh
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

        var manualEvents = AppState.ManualEvents
            .Select(ToSchulnetzEvent)
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

    private static SchulnetzEvent ToSchulnetzEvent(ManualEventData m)
        => new(
            Key:      "MANUAL_" + m.Id.ToString("N"),
            RawUid:   "MANUAL_" + m.Id.ToString("N"),
            Type:     m.TypeKey == "Pruefung" ? SchulnetzEventType.Pruefung : SchulnetzEventType.Termin,
            Start:    m.Start,
            End:      m.End,
            IsAllDay: m.IsAllDay,
            Summary:  m.Title,
            Location: m.Location);

    // ══════════════════════════════════════════════════════════════════════
    // MONATSANSICHT
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

        // ── Tagesnummer (adaptive Opacity statt hard-coded Farbe) ──
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
            // Opacity statt fixer Farbe → passt sich automatisch an Light/Dark an
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

        var timePrefix = ev.IsAllDay ? "" : ev.Start.LocalDateTime.ToString("H:mm ", _deCH);
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
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
    // WOCHENANSICHT — Zeit-Raster (Stundenplan-Ansicht)
    // ══════════════════════════════════════════════════════════════════════
    private void BuildWeekView()
    {
        MonthDayHeaders.Visibility = Visibility.Collapsed;
        CalendarGrid.Visibility    = Visibility.Collapsed;
        WeekGrid.Visibility        = Visibility.Visible;

        // Alle vorherigen Children + Definitionen löschen
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

        // Haupt-Layout: 2 Zeilen (Header + Zeitraster)
        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── Kopfzeile (Wochentag-Nummern) ────────────────────────────────────
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_gutterW) });
        for (int d = 0; d < 7; d++)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Gutter-Spacer
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

        // ── Zeitraster (scrollbar) ───────────────────────────────────────────
        int totalSlots = (_weekEndH - _weekStartH) * (60 / _slotMin); // 60 Slots bei 15-min / 900px

        var tGrid = new Grid();
        tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_gutterW) });
        for (int d = 0; d < 7; d++)
            tGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i <= totalSlots; i++)
            tGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_slotPx) });

        // Stunden-Linien und Zeitbeschriftung
        int hourCount = _weekEndH - _weekStartH;
        for (int h = 0; h <= hourCount; h++)
        {
            int slotRow = h * (60 / _slotMin);
            int hour    = _weekStartH + h;

            // Linie über alle Tagesspalten
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

            // Halbe-Stunden-Linie (schwächer)
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

            // Zeitbeschriftung links
            if (h < hourCount)
            {
                var lbl = new WpfTextBlock
                {
                    Text                = $"{hour:D2}:00",
                    FontSize            = 9,
                    Opacity             = 0.42,
                    VerticalAlignment   = VerticalAlignment.Top,
                    HorizontalAlignment = WpfHA.Right,
                    Margin              = new Thickness(0, -6, 5, 0),
                    IsHitTestVisible    = false
                };
                Grid.SetRow(lbl, slotRow);
                Grid.SetColumn(lbl, 0);
                tGrid.Children.Add(lbl);
            }
        }

        // Vertikale Spalten-Trennlinien
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

            // Wochenende / Heute Hintergrundtönung
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

        // Events in die Spalten zeichnen
        for (int d = 0; d < 7; d++)
        {
            var date      = weekStart.AddDays(d);
            var dayEvents = events.Where(e => e.Start.Date == date).OrderBy(e => e.Start).ToList();

            // Ganztägige Events werden als kompakte Streifen am Tagesstart gezeigt
            int allDayRow = 0; // Startzeile für ganztägige Events (oben im Raster)
            foreach (var ev in dayEvents.Where(e => e.IsAllDay))
            {
                var strip = MakeWeekAllDayStrip(ev);
                Grid.SetRow(strip, allDayRow);
                Grid.SetColumn(strip, d + 1);
                tGrid.Children.Add(strip);
                allDayRow = Math.Min(allDayRow + 1, totalSlots - 1);
            }

            // Zeitgebundene Events
            foreach (var ev in dayEvents.Where(e => !e.IsAllDay))
            {
                var local      = ev.Start.LocalDateTime;
                var localEnd   = ev.End.LocalDateTime;
                double startH  = local.Hour + local.Minute / 60.0;
                double endH    = localEnd.Hour + localEnd.Minute / 60.0;

                // Ausserhalb des Rasters → überspringen
                if (endH <= _weekStartH || startH >= _weekEndH) continue;
                startH = Math.Max(startH, _weekStartH);
                endH   = Math.Min(endH,   _weekEndH);

                double offsetH  = startH - _weekStartH;
                double slotDbl  = offsetH * (60.0 / _slotMin);
                int    startSlot = (int)slotDbl;
                double marginTop = (slotDbl - startSlot) * _slotPx;

                double durH      = endH - startH;
                int    spanSlots = Math.Max(1, (int)Math.Round(durH * (60.0 / _slotMin)));
                // Nicht über Rastergrenze hinausgehen
                if (startSlot + spanSlots > totalSlots)
                    spanSlots = totalSlots - startSlot;

                var card = MakeWeekEventCard(ev, marginTop);
                Grid.SetRow(card, startSlot);
                Grid.SetRowSpan(card, spanSlots);
                Grid.SetColumn(card, d + 1);
                tGrid.Children.Add(card);
            }
        }

        // Aktueller Zeitindikator (roter Strich)
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

                var dot = new WpfBorder
                {
                    Width             = 8,
                    Height            = 8,
                    CornerRadius      = new CornerRadius(4),
                    Background        = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin            = new Thickness(-4, mTop - 4, 0, 0),
                    IsHitTestVisible  = false
                };
                Grid.SetRow(dot, nowSlot);
                Grid.SetColumn(dot, todayCol);
                tGrid.Children.Add(dot);

                var nowLine = new WpfBorder
                {
                    Height            = 2,
                    Background        = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin            = new Thickness(0, mTop - 1, 0, 0),
                    IsHitTestVisible  = false
                };
                Grid.SetRow(nowLine, nowSlot);
                Grid.SetColumn(nowLine, todayCol);
                tGrid.Children.Add(nowLine);
            }
        }

        // ScrollViewer — scrollt automatisch zur aktuellen Uhrzeit
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = tGrid
        };
        scroll.Loaded += (_, _) =>
        {
            double targetH  = Math.Max(_weekStartH, Math.Min(_weekEndH - 2, DateTime.Now.TimeOfDay.TotalHours));
            double offsetPx = (targetH - _weekStartH) * (60.0 / _slotMin) * _slotPx - 80;
            scroll.ScrollToVerticalOffset(Math.Max(0, offsetPx));
        };

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

    private UIElement MakeWeekEventCard(SchulnetzEvent ev, double topMargin = 0)
    {
        var  color = GetEventColor(ev);
        bool isSel = ev == _selectedEvent;

        var card = new WpfBorder
        {
            Background        = new SolidColorBrush(
                                    Color.FromArgb(isSel ? (byte)220 : (byte)190,
                                                   color.R, color.G, color.B)),
            CornerRadius      = new CornerRadius(4),
            Margin            = new Thickness(2, topMargin + 1, 2, 1),
            Padding           = new Thickness(5, 3, 5, 3),
            Cursor            = WpfCursors.Hand,
            Tag               = ev,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds      = true
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
        inner.Children.Add(new WpfTextBlock
        {
            Text         = ev.Summary,
            FontSize     = 10,
            FontWeight   = FontWeights.SemiBold,
            Foreground   = WpfBrushes.White,
            TextWrapping = TextWrapping.Wrap
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

        card.Child = inner;
        card.MouseLeftButtonUp += EventPill_MouseUp;
        return card;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Panel-Steuerung
    // ══════════════════════════════════════════════════════════════════════

    private void OpenPanel(string mode, int width = 340)
    {
        _panelMode                 = mode;
        DetailPanel.Visibility     = Visibility.Visible;
        DetailColumnDef.Width      = new GridLength(width);

        // Inhalt-Sektionen
        DetailContent.Visibility   = mode == "Detail"   ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = mode == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        AddEventSection.Visibility = mode == "AddEvent" ? Visibility.Visible : Visibility.Collapsed;

        // Footer-Sektionen
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
    // DETAIL-PANEL
    // ══════════════════════════════════════════════════════════════════════
    private void ShowDetailPanel(SchulnetzEvent ev)
    {
        var  color      = GetEventColor(ev);
        var  colorKey   = GetColorKey(ev);
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;
        bool isLektion  = ev.Type == SchulnetzEventType.Lektion;
        bool isManual   = ev.Key.StartsWith("MANUAL_", StringComparison.Ordinal);

        // Panel-Titel
        TxtPanelTitle.Text       = isPruefung ? "⚠  PRÜFUNG" : isLektion ? "📘  STUNDE" : "📌  TERMIN";
        TxtPanelTitle.Foreground = new SolidColorBrush(color);
        BtnDeleteFromApp.Tag     = ev;

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
            FontSize     = 16,
            FontWeight   = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 16)
        });

        // Datum
        DetailContent.Children.Add(MakeDetailRow("📅",
            ev.Start.LocalDateTime.ToString("dddd, d. MMMM yyyy", _deCH)));

        // Zeit
        var timeStr = ev.IsAllDay ? "Ganztägig"
            : $"{ev.Start.LocalDateTime:HH:mm} – {ev.End.LocalDateTime:HH:mm} Uhr";
        DetailContent.Children.Add(MakeDetailRow("🕐", timeStr));

        // Ort
        if (!string.IsNullOrWhiteSpace(ev.Location))
            DetailContent.Children.Add(MakeDetailRow("📍", ev.Location!));

        // Typ-Hinweis
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

        // ── Farbe ändern ──
        var colorLabel = isLektion
            ? $"Farbe für alle {colorKey}-Stunden"
            : isPruefung ? "Farbe für alle Prüfungen"
            :              "Farbe für alle Termine";

        DetailContent.Children.Add(new WpfTextBlock
        {
            Text       = colorLabel,
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Opacity    = 0.75,
            Margin     = new Thickness(0, 0, 0, 8)
        });

        // Farbpalette
        var paletteWrap = new WpfWrapPanel { Orientation = WpfOrientation.Horizontal };
        foreach (var (hex, name) in _palette)
        {
            var dot = new WpfBorder
            {
                Width        = 24,
                Height       = 24,
                CornerRadius = new CornerRadius(12),
                Margin       = new Thickness(0, 0, 6, 6),
                Cursor       = WpfCursors.Hand,
                Background   = new SolidColorBrush(
                                   (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)),
                ToolTip      = name,
                Tag          = (colorKey, hex)
            };

            // Aktuelle Farbe markieren
            var currentHex = AppState.GetEventColor(colorKey);
            if (string.Equals(hex, currentHex, StringComparison.OrdinalIgnoreCase))
            {
                dot.BorderThickness = new Thickness(2);
                dot.BorderBrush     = WpfBrushes.White;
                dot.Width           = 22;
                dot.Height          = 22;
            }

            dot.MouseLeftButtonUp += ColorDot_Click;
            paletteWrap.Children.Add(dot);
        }
        DetailContent.Children.Add(paletteWrap);

        // Löschen für manuelle Events
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

    // ══════════════════════════════════════════════════════════════════════
    // EINSTELLUNGS-PANEL
    // ══════════════════════════════════════════════════════════════════════
    private void ShowSettingsPanel()
    {
        TxtPanelTitle.Text       = "⚙  KALENDER-EINSTELLUNGEN";
        TxtPanelTitle.SetResourceReference(WpfTextBlock.ForegroundProperty, "SystemControlForegroundBaseHighBrush");
        _selectedEvent           = null;

        SettingsContent.Children.Clear();

        // ─ Kategoriefarben ──────────────────────────────────────────────
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

        // Zeige individuelle Fach-Farben
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

        // Separator
        SettingsContent.Children.Add(new Separator
        {
            Margin = new Thickness(0, 20, 0, 16),
            Style  = (Style)FindResource("Divider")
        });

        // ─ Kalender-Aktionen ─────────────────────────────────────────────
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
        // Settings-Panel neu aufbauen (nach Reset)
        ShowSettingsPanel();
    }

    private UIElement MakeCategoryColorRow(string key, string label)
    {
        var hex = AppState.GetEventColor(key);

        // Vertikales Layout: Label oben, Palette darunter — kein Abschneiden
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
    // ADD-EVENT-PANEL
    // ══════════════════════════════════════════════════════════════════════
    private void ShowAddEventPanel()
    {
        TxtPanelTitle.Text       = "➕  NEUER EINTRAG";
        TxtPanelTitle.SetResourceReference(WpfTextBlock.ForegroundProperty, "SystemControlForegroundBaseHighBrush");
        _selectedEvent           = null;

        // Formular zurücksetzen
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

    // ── Event-Handler ────────────────────────────────────────────────────────

    private void ChkNewAllDay_Changed(object sender, RoutedEventArgs e)
    {
        TimePickers.Visibility = ChkNewAllDay.IsChecked == true
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnSaveManualEvent_Click(object sender, RoutedEventArgs e)
    {
        // Validierung
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

        AppState.AddManualEvent(manualEvent); // löst Notify → Refresh aus
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
    // Farb-Klick Handler
    // ══════════════════════════════════════════════════════════════════════
    private void ColorDot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfBorder dot) return;
        if (dot.Tag is not (string key, string hex)) return;

        AppState.SetCategoryColor(key, hex); // löst Notify → Refresh aus

        // Panel neu aufbauen
        if (_panelMode == "Settings")
            ShowSettingsPanel();
        else if (_selectedEvent != null)
            ShowDetailPanel(_selectedEvent);

        e.Handled = true;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Event-Handler (Navigation, Filter, Buttons)
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
        if (!ev.Key.StartsWith("MANUAL_", StringComparison.Ordinal)) return;

        if (!Confirm($"«{ev.Summary}» wirklich löschen?")) return;

        var id = Guid.ParseExact(ev.Key["MANUAL_".Length..], "N");
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

    // ── Navigation ──────────────────────────────────────────────────────────

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
        if (!_selectedDate.HasValue) _selectedDate = DateTime.Today;
        Refresh();
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn) return;
        _filter        = btn.Tag as string ?? "All";
        _selectedEvent = null;
        if (_panelMode == "Detail") ClosePanel();

        BtnFilterAll.Style     = (Style)FindResource(_filter == "All"      ? "PrimaryButton" : "SecondaryButton");
        BtnFilterPruefung.Style = (Style)FindResource(_filter == "Pruefung" ? "PrimaryButton" : "SecondaryButton");
        BtnFilterTermin.Style   = (Style)FindResource(_filter == "Termin"   ? "PrimaryButton" : "SecondaryButton");
        BtnFilterLektion.Style  = (Style)FindResource(_filter == "Lektion"  ? "PrimaryButton" : "SecondaryButton");

        Refresh();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
}
