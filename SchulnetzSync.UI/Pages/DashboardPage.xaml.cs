using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Model;
using SchulnetzSync.UI.Model;
using SchulnetzSync.UI.Services;
using CheckBox            = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation         = System.Windows.Controls.Orientation;

namespace SchulnetzSync.UI.Pages;

/// <summary>
/// The landing page: status of the last run, a few numbers, what is coming up, today's
/// timetable and the open tasks.
///
/// The page owns no data of its own. It reads everything from <see cref="AppState"/> and
/// rebuilds itself whenever that fires Changed, which keeps it honest but means every
/// build has to be cheap.
/// </summary>
public partial class DashboardPage : Page
{
    private static readonly CultureInfo DeCh = new("de-CH");

    /// <summary>How far ahead the "Als Nächstes" preview looks.</summary>
    private const int UpcomingDays = 7;

    /// <summary>Most open tasks the side column will show.</summary>
    private const int MaxTasksShown = 5;

    /// <summary>Below this width the side column drops underneath the main one.</summary>
    private const double TwoColumnMinWidth = 1000;

    private static readonly Color Green  = Color.FromRgb(0x22, 0xC5, 0x5E);
    private static readonly Color Amber  = Color.FromRgb(0xF5, 0x9E, 0x0B);
    private static readonly Color Red    = Color.FromRgb(0xEF, 0x44, 0x44);
    private static readonly Color Accent = Color.FromRgb(0x5C, 0x6E, 0xF7);
    private static readonly Color Gray   = Color.FromRgb(0x8A, 0x8F, 0x98);

    private readonly SyncService _sync = new();
    private CancellationTokenSource? _cts;

    /// <summary>Remembers the last loading state, so the start refresh is logged once and not on every tick.</summary>
    private bool _wasRefreshingFeed;

    public DashboardPage()
    {
        InitializeComponent();
        _sync.ProgressReceived += msg => Dispatcher.Invoke(() => AppendLog(msg));
        _sync.Completed        += result => Dispatcher.Invoke(() => OnSyncCompleted(result));
        _sync.Failed           += ex => Dispatcher.Invoke(() =>
        {
            AppendLog("❌ " + ex.Message);
            RefreshStatus();
        });

        // AppState.Changed comes off a background thread, so always go through the dispatcher.
        Loaded      += (_, _) => { AppState.Changed += OnStateChanged; RefreshStatus(); };
        Unloaded    += (_, _) => AppState.Changed -= OnStateChanged;
        SizeChanged += (_, _) => ApplyLayout();
    }

    private void OnStateChanged() => Dispatcher.Invoke(RefreshStatus);

    // ── Buttons ─────────────────────────────────────────────────────────

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
        => await StartSyncAsync(dryRun: false);

    private async void BtnDryRun_Click(object sender, RoutedEventArgs e)
        => await StartSyncAsync(dryRun: true);

    private void BtnGoSettings_Click(object sender, RoutedEventArgs e) => Navigate("Settings");
    private void BtnGoEvents_Click(object sender, RoutedEventArgs e)   => Navigate("Events");
    private void BtnGoTasks_Click(object sender, RoutedEventArgs e)    => Navigate("Tasks");

    private void Navigate(string tag)
    {
        if (Window.GetWindow(this) is MainWindow main)
            main.NavigateTo(tag);
    }

    private void BtnToggleLog_Click(object sender, RoutedEventArgs e)
        => SetLogExpanded(LogBody.Visibility != Visibility.Visible);

    private void SetLogExpanded(bool expanded)
    {
        LogBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        LogChevron.Text    = expanded ? "\uE70E" : "\uE70D";
        if (expanded) LogScroll.ScrollToBottom();
    }

    // ── Sync ────────────────────────────────────────────────────────────

    private async Task StartSyncAsync(bool dryRun)
    {
        if (AppState.IsSyncing || AppState.IsRefreshingFeed) return;
        TxtLog.Text     = dryRun ? "👁  Feed wird geladen…" : "⏳ Synchronisation startet…";
        TxtLogLast.Text = TxtLog.Text;
        _cts = new CancellationTokenSource();
        await _sync.RunAsync(dryRun, _cts.Token);
    }

    private void OnSyncCompleted(SyncResult result) => RefreshStatus();

    // ── Building the page ───────────────────────────────────────────────

    private void RefreshStatus()
    {
        var config      = AppState.Config;
        bool hasFeed    = ConfigManager.GetFeedUrl(config) is not null;
        bool hasOutlook = MicrosoftAccount.IsAvailable(config);
        bool busy       = AppState.IsSyncing || AppState.IsRefreshingFeed;
        var now         = DateTimeOffset.Now;

        LogAutoRefreshTransition(config);

        SetupHint.Visibility = hasFeed ? Visibility.Collapsed : Visibility.Visible;

        // The Outlook button only makes sense with an account behind it.
        BtnSync.Visibility     = hasOutlook ? Visibility.Visible : Visibility.Collapsed;
        OutlookChip.Visibility = hasOutlook ? Visibility.Visible : Visibility.Collapsed;
        BtnSync.IsEnabled      = hasFeed && hasOutlook && !busy;
        BtnDryRun.IsEnabled    = hasFeed && !busy;

        RefreshHeader(hasFeed);
        RefreshStatusCard(config, busy);
        RefreshStats(now);
        BuildUpcoming(config, now);
        BuildSchedule(now);
        BuildTasks(now);
        ApplyLayout();
    }

    private void RefreshHeader(bool hasFeed)
    {
        TxtGreeting.Text = DateTime.Now.Hour switch
        {
            < 11 => "Guten Morgen",
            < 17 => "Guten Tag",
            _    => "Guten Abend",
        };

        var date = DateTime.Today.ToString("dddd, d. MMMM", DeCh);
        TxtHeaderSub.Text = hasFeed ? date : date + " · Fast fertig, es fehlt nur noch die Feed-URL.";
    }

    /// <summary>Two columns when there is room, otherwise everything stacked.</summary>
    private void ApplyLayout()
    {
        bool wide = ActualWidth <= 0 || ActualWidth >= TwoColumnMinWidth;

        SideColumn.Width = wide ? new GridLength(360) : new GridLength(0);
        Grid.SetRow(SidePanel,    wide ? 0 : 1);
        Grid.SetColumn(SidePanel, wide ? 1 : 0);

        StatGrid.Columns = ActualWidth is > 0 and < 760 ? 2 : 4;
    }

    // ── Status card ─────────────────────────────────────────────────────

    /// <summary>
    /// The refresh at start runs quietly in the background, so the state change is written
    /// to the log on screen — otherwise nothing at all seems to be happening.
    /// </summary>
    private void LogAutoRefreshTransition(SyncConfig config)
    {
        if (!AppState.IsSyncing)
        {
            if (AppState.IsRefreshingFeed && !_wasRefreshingFeed)
                AppendLog("⏳ Feed wird im Hintergrund geladen…");
            else if (!AppState.IsRefreshingFeed && _wasRefreshingFeed)
                AppendLog(config.LastFeedRefreshAt.HasValue
                    ? $"✅ Feed geladen. {AppState.CachedFeedEvents.Count} Einträge."
                    : "⚠️ Feed nicht erreichbar, zeige zwischengespeicherte Daten.");
        }
        _wasRefreshingFeed = AppState.IsRefreshingFeed;
    }

    private void RefreshStatusCard(SyncConfig config, bool busy)
    {
        TxtLastFeed.Text = config.LastFeedRefreshAt.HasValue
            ? $"Feed {FormatAgo(DateTimeOffset.Now - config.LastFeedRefreshAt.Value)}"
            : "Feed noch nie geladen";

        TxtLastRun.Text = config.LastRunAt.HasValue
            ? $"Outlook {FormatAgo(DateTimeOffset.Now - config.LastRunAt.Value)}"
            : "Outlook noch nie synchronisiert";

        TxtLastResult.Text       = config.LastRunResult ?? "";
        TxtLastResult.Visibility = string.IsNullOrWhiteSpace(config.LastRunResult)
            ? Visibility.Collapsed : Visibility.Visible;

        SyncRing.IsActive      = busy;
        StatusGlyph.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;

        if (busy)
        {
            SetStatus(Accent, "", AppState.IsSyncing ? "Synchronisation läuft…" : "Feed wird geladen…");
            return;
        }

        // The feed timestamp says more: it is refreshed on every start, the sync one is not.
        var reference = config.LastFeedRefreshAt ?? config.LastRunAt;
        if (reference is null)
        {
            SetStatus(Amber, "\uE823", "Noch nichts geladen");
            return;
        }

        bool fresh = DateTimeOffset.Now - reference.Value < TimeSpan.FromHours(25);
        if (fresh) SetStatus(Green, "\uE73E", "Alles aktuell");
        else       SetStatus(Amber, "\uE7BA", "Aktualisierung fällig");
    }

    private void SetStatus(Color color, string glyph, string label)
    {
        StatusDisc.Fill        = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
        StatusGlyph.Foreground = new SolidColorBrush(color);
        StatusGlyph.Text       = glyph;
        TxtStatusLabel.Text    = label;
    }

    // ── The numbers at the top ──────────────────────────────────────────

    private void RefreshStats(DateTimeOffset now)
    {
        var visible = VisibleEvents().ToList();

        FillCountTile(visible, SchulnetzEventType.Pruefung, now, TxtStatPruefung, TxtStatPruefungSub);
        FillCountTile(visible, SchulnetzEventType.Termin,   now, TxtStatTermin,   TxtStatTerminSub);

        // Lessons today
        var today = Lessons().Where(l => l.Start.Date == DateTime.Today).OrderBy(l => l.Start).ToList();
        TxtStatLessons.Text = today.Count.ToString();
        TxtStatLessonsSub.Text = today.Count switch
        {
            0                                   => "Kein Unterricht",
            _ when today[^1].End <= now         => "Schulschluss war " + today[^1].End.ToString("HH:mm"),
            _ when today[0].Start > now         => "Beginn " + today[0].Start.ToString("HH:mm"),
            _                                   => "Schluss " + today[^1].End.ToString("HH:mm"),
        };

        // Tasks
        var open    = AppState.Tasks.Where(t => !t.IsDone).ToList();
        int overdue = open.Count(t => t.IsOverdue(now));
        int dueToday = open.Count(t => !t.IsOverdue(now) && t.DueAt?.Date == DateTime.Today);
        TxtStatTasks.Text = open.Count.ToString();
        TxtStatTasksSub.Text = (overdue, dueToday) switch
        {
            (> 0, _) => overdue == 1 ? "1 überfällig" : $"{overdue} überfällig",
            (_, > 0) => $"{dueToday} heute fällig",
            _        => open.Count == 0 ? "Alles erledigt" : "Nichts dringend",
        };
        if (overdue > 0) TxtStatTasksSub.Foreground = new SolidColorBrush(Red);
        else             TxtStatTasksSub.ClearValue(TextBlock.ForegroundProperty);
        TxtStatTasksSub.Opacity = overdue > 0 ? 1.0 : 0.6;
    }

    private static void FillCountTile(List<SchulnetzEvent> events, SchulnetzEventType type,
                                      DateTimeOffset now, TextBlock number, TextBlock sub)
    {
        if (AppState.CachedFeedEvents.Count == 0 && !events.Any(e => e.Type == type))
        {
            number.Text = "·";
            sub.Text    = "Noch keine Daten";
            return;
        }

        var coming = events.Where(e => e.Type == type && e.End >= now).OrderBy(e => e.Start).ToList();
        number.Text = coming.Count.ToString();
        sub.Text    = coming.Count == 0 ? "Keine anstehend" : "Nächste " + FormatCountdown(coming[0].Start);
    }

    // ── "Als Nächstes" ──────────────────────────────────────────────────

    /// <summary>
    /// Shows the same thing that gets synced — exams and appointments according to
    /// <see cref="SyncConfig.EnabledTypes"/> — grouped by day.
    /// </summary>
    private void BuildUpcoming(SyncConfig config, DateTimeOffset now)
    {
        bool showPruefungen = config.EnabledTypes.Contains(SchulnetzEventType.Pruefung);
        bool showTermine    = config.EnabledTypes.Contains(SchulnetzEventType.Termin);

        TxtUpcomingTitle.Text = (showPruefungen, showTermine) switch
        {
            (true, true)  => $"Prüfungen und Termine der nächsten {UpcomingDays} Tage",
            (true, false) => $"Prüfungen der nächsten {UpcomingDays} Tage",
            (false, true) => $"Termine der nächsten {UpcomingDays} Tage",
            _             => "Prüfungen und Termine sind in den Einstellungen ausgeschaltet",
        };

        // Take the offset of the target day itself. Using today's falls apart the moment
        // the window reaches across the daylight-saving switch.
        var windowEnd = new DateTimeOffset(DateTime.Today.AddDays(UpcomingDays + 1));
        var items = VisibleEvents()
            .Where(e => (e.Type == SchulnetzEventType.Pruefung && showPruefungen)
                     || (e.Type == SchulnetzEventType.Termin   && showTermine))
            .Where(e => e.End >= now && e.Start < windowEnd)
            .OrderBy(e => e.Start)
            .ThenBy(e => e.Summary, StringComparer.CurrentCulture)
            .ToList();

        UpcomingList.Children.Clear();
        if (items.Count == 0)
        {
            UpcomingEmpty.Visibility = Visibility.Visible;
            TxtUpcomingEmpty.Text = AppState.CachedFeedEvents.Count == 0
                ? "Noch keine Daten geladen."
                : $"Nichts in den nächsten {UpcomingDays} Tagen. Freie Bahn!";
            return;
        }

        UpcomingEmpty.Visibility = Visibility.Collapsed;
        foreach (var day in items.GroupBy(e => e.Start.Date))
        {
            UpcomingList.Children.Add(DayHeader(day.Key, UpcomingList.Children.Count == 0));
            foreach (var ev in day)
                UpcomingList.Children.Add(UpcomingRow(ev));
        }
    }

    private static UIElement DayHeader(DateTime day, bool first)
    {
        var grid = new Grid { Margin = new Thickness(0, first ? 4 : 14, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text       = FormatDayLong(day),
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity    = 0.6,
        });

        var countdown = new TextBlock
        {
            Text     = FormatCountdown(new DateTimeOffset(day)),
            FontSize = 12,
            Opacity  = 0.45,
        };
        Grid.SetColumn(countdown, 1);
        grid.Children.Add(countdown);
        return grid;
    }

    private static UIElement UpcomingRow(SchulnetzEvent ev)
    {
        var color      = ResolveColor(ev);
        bool isPruefung = ev.Type == SchulnetzEventType.Pruefung;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border
        {
            Background   = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
        };
        grid.Children.Add(bar);

        var when = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        when.Children.Add(new TextBlock
        {
            Text       = ev.IsAllDay ? "Ganzer Tag" : ev.Start.ToString("HH:mm"),
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
        });
        if (!ev.IsAllDay)
            when.Children.Add(new TextBlock { Text = "bis " + ev.End.ToString("HH:mm"), FontSize = 11.5, Opacity = 0.55 });
        Grid.SetColumn(when, 1);
        grid.Children.Add(when);

        var what = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        what.Children.Add(new TextBlock
        {
            Text         = ShortTitle(ev),
            ToolTip      = ev.Summary,
            FontSize     = 13.5,
            FontWeight   = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(ev.Location))
            what.Children.Add(IconLine("", ev.Location));
        Grid.SetColumn(what, 2);
        grid.Children.Add(what);

        var kind = Pill(isPruefung ? "Prüfung" : "Termin", color);
        kind.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(kind, 3);
        grid.Children.Add(kind);

        return new Border
        {
            Child        = grid,
            Padding      = new Thickness(10, 9, 10, 9),
            Margin       = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(10),
            Background   = new SolidColorBrush(Color.FromArgb(0x12, 0x80, 0x80, 0x80)),
        };
    }

    // ── Timetable ───────────────────────────────────────────────────────

    /// <summary>
    /// Shows today for as long as there are lessons left. After that, and on days off,
    /// it shows the next school day — so in the evening you already see tomorrow.
    /// </summary>
    private void BuildSchedule(DateTimeOffset now)
    {
        ScheduleList.Children.Clear();

        var byDay = Lessons()
            .Where(l => l.Start.Date >= DateTime.Today)
            .GroupBy(l => l.Start.Date)
            .OrderBy(g => g.Key)
            .FirstOrDefault(g => g.Key > DateTime.Today || g.Any(l => l.End > now));

        if (byDay is null)
        {
            TxtScheduleTitle.Text      = "Stundenplan";
            TxtScheduleSub.Text        = "";
            TxtScheduleEmpty.Text      = AppState.CachedFeedEvents.Count == 0
                ? "Noch keine Daten geladen."
                : "Keine kommenden Stunden im Feed.";
            TxtScheduleEmpty.Visibility = Visibility.Visible;
            return;
        }

        TxtScheduleEmpty.Visibility = Visibility.Collapsed;
        var lessons = byDay.OrderBy(l => l.Start).ThenBy(l => l.Summary, StringComparer.CurrentCulture).ToList();

        TxtScheduleTitle.Text = byDay.Key == DateTime.Today ? "Heute im Stundenplan"
                              : byDay.Key == DateTime.Today.AddDays(1) ? "Morgen im Stundenplan"
                              : byDay.Key.ToString("dddd", DeCh) + " im Stundenplan";
        TxtScheduleSub.Text = $"{lessons.Count} {(lessons.Count == 1 ? "Stunde" : "Stunden")} · "
                            + $"{lessons[0].Start:HH:mm} bis {lessons[^1].End:HH:mm}"
                            + (byDay.Key > DateTime.Today.AddDays(1) ? " · " + byDay.Key.ToString("d. MMMM", DeCh) : "");

        foreach (var lesson in lessons)
            ScheduleList.Children.Add(LessonRow(lesson, now));
    }

    private static UIElement LessonRow(SchulnetzEvent lesson, DateTimeOffset now)
    {
        var color  = LessonColor(lesson);
        bool past  = lesson.End <= now;
        bool live  = lesson.Start <= now && now < lesson.End;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text              = lesson.Start.ToString("HH:mm"),
            FontSize          = 12.5,
            FontWeight        = FontWeights.SemiBold,
            Opacity           = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var dot = new Border
        {
            Width             = 10,
            Height            = 10,
            CornerRadius      = new CornerRadius(3),
            Background        = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(dot, 1);
        grid.Children.Add(dot);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock
        {
            Text         = ShortTitle(lesson),
            ToolTip      = lesson.Summary,
            FontSize     = 13,
            FontWeight   = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(lesson.Location))
            body.Children.Add(IconLine("", lesson.Location));
        Grid.SetColumn(body, 2);
        grid.Children.Add(body);

        if (live)
        {
            var pill = Pill("Jetzt", Accent);
            pill.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pill, 3);
            grid.Children.Add(pill);
        }

        return new Border
        {
            Child        = grid,
            Padding      = new Thickness(8, 6, 8, 6),
            Margin       = new Thickness(-8, 0, -8, 2),
            CornerRadius = new CornerRadius(9),
            Opacity      = past ? 0.45 : 1.0,
            Background   = live
                ? new SolidColorBrush(Color.FromArgb(0x22, Accent.R, Accent.G, Accent.B))
                : System.Windows.Media.Brushes.Transparent,
        };
    }

    // ── Tasks column ────────────────────────────────────────────────────

    private void BuildTasks(DateTimeOffset now)
    {
        TaskList.Children.Clear();

        var open = AppState.Tasks.Where(t => !t.IsDone).ToList();
        var shown = open
            .OrderByDescending(t => t.IsOverdue(now))
            .ThenBy(t => t.DueAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(t => t.IsImportant)
            .ThenBy(t => t.CreatedAt)
            .Take(MaxTasksShown)
            .ToList();

        TxtTasksSub.Text = open.Count switch
        {
            0 => "",
            _ when open.Count > shown.Count => $"Die dringendsten {shown.Count} von {open.Count}",
            1 => "1 offene Aufgabe",
            _ => $"{open.Count} offene Aufgaben",
        };
        TxtTasksSub.Visibility = open.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        TxtTasksEmpty.Visibility = open.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var task in shown)
            TaskList.Children.Add(TaskRow(task, now));
    }

    private UIElement TaskRow(TaskItem task, DateTimeOffset now)
    {
        var listColor = string.IsNullOrWhiteSpace(task.ListName) ? Gray : ParseColor(AppState.TaskListColor(task.ListName), Gray);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var check = new CheckBox
        {
            Style             = (Style)FindResource("RoundCheck"),
            BorderBrush       = new SolidColorBrush(listColor),
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 1, 12, 0),
            ToolTip           = "Erledigt",
        };
        check.Checked += (_, _) => AppState.UpdateTask(task with { IsDone = true, CompletedAt = DateTimeOffset.Now });
        grid.Children.Add(check);

        var body = new StackPanel();
        var title = new TextBlock
        {
            FontSize     = 13.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (task.IsImportant)
            title.Inlines.Add(new System.Windows.Documents.Run("★ ") { Foreground = new SolidColorBrush(Amber) });
        title.Inlines.Add(new System.Windows.Documents.Run(task.Title));
        body.Children.Add(title);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        if (!string.IsNullOrWhiteSpace(task.ListName))
            meta.Children.Add(new TextBlock { Text = task.ListName, FontSize = 12, Opacity = 0.55 });

        if (task.DueAt is { } due)
        {
            if (meta.Children.Count > 0)
                meta.Children.Add(new TextBlock { Text = "  ·  ", FontSize = 12, Opacity = 0.35 });

            bool overdue = task.IsOverdue(now);
            var dueText = new TextBlock
            {
                Text     = overdue ? "Überfällig, " + FormatDueDay(due) : FormatDueDay(due),
                FontSize = 12,
            };
            if (overdue) { dueText.Foreground = new SolidColorBrush(Red); dueText.FontWeight = FontWeights.SemiBold; }
            else if (due.Date <= DateTime.Today.AddDays(1)) dueText.Foreground = new SolidColorBrush(Amber);
            else dueText.Opacity = 0.55;
            meta.Children.Add(dueText);
        }
        if (meta.Children.Count > 0) body.Children.Add(meta);

        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    // ── Getting at the data ─────────────────────────────────────────────

    /// <summary>Short form, same as in the calendar: "TEU" or "FRA · Examen de grammaire".</summary>
    private static string ShortTitle(SchulnetzEvent ev)
    {
        var (code, rest) = SubjectCode.Split(ev.Summary);
        if (code.Length == 0) return ev.Summary;
        return rest.Length == 0 ? code : $"{code} · {rest}";
    }

    /// <summary>Feed entries plus hand-made ones, minus everything the user hid.</summary>
    private static IEnumerable<SchulnetzEvent> VisibleEvents()
    {
        var suppressed = AppState.SuppressedKeys;
        return AppState.CachedFeedEvents.Concat(AppState.ManualAsEvents())
            .Where(e => !suppressed.Contains(e.Key));
    }

    private static IEnumerable<SchulnetzEvent> Lessons()
        => VisibleEvents().Where(e => e.Type == SchulnetzEventType.Lektion && !e.IsAllDay);

    /// <summary>Colour from the user settings, falling back to the default.</summary>
    private static Color ResolveColor(SchulnetzEvent ev)
    {
        var key      = ev.Type == SchulnetzEventType.Pruefung ? "Pruefung" : "Termin";
        var fallback = ev.Type == SchulnetzEventType.Pruefung
            ? Color.FromRgb(0xDC, 0x26, 0x26)
            : Color.FromRgb(0xD9, 0x77, 0x06);
        return ParseColor(AppState.GetEventColor(ev.Key, key), fallback);
    }

    /// <summary>The same colour the calendar uses: individual, then subject, then default.</summary>
    private static Color LessonColor(SchulnetzEvent lesson)
        => ParseColor(AppState.GetEventColor(lesson.Key, SubjectCode.FromSummary(lesson.Summary)),
                      Color.FromRgb(0x25, 0x63, 0xEB));

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException) { return fallback; }
    }

    // ── Building blocks ─────────────────────────────────────────────────

    private static Border Pill(string text, Color color) => new()
    {
        Background   = new SolidColorBrush(Color.FromArgb(0x2A, color.R, color.G, color.B)),
        CornerRadius = new CornerRadius(9),
        Padding      = new Thickness(8, 2, 8, 3),
        Child = new TextBlock
        {
            Text       = text,
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Lighten(color)),
        },
    };

    /// <summary>A little lighter, so coloured text stays readable on a dark background.</summary>
    private static Color Lighten(Color c)
        => Color.FromRgb((byte)(c.R + (255 - c.R) * 0.25), (byte)(c.G + (255 - c.G) * 0.25), (byte)(c.B + (255 - c.B) * 0.25));

    private static StackPanel IconLine(string glyph, string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0), Opacity = 0.6 };
        line.Children.Add(new TextBlock
        {
            Text              = glyph,
            FontFamily        = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize          = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 1, 5, 0),
        });
        line.Children.Add(new TextBlock
        {
            Text         = text,
            FontSize     = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return line;
    }

    // ── Wording ─────────────────────────────────────────────────────────

    private static string FormatDayLong(DateTime day)
    {
        var days = (day.Date - DateTime.Today).Days;
        return days switch
        {
            0 => "HEUTE",
            1 => "MORGEN",
            _ => day.ToString("dddd, d. MMMM", DeCh).ToUpper(DeCh),
        };
    }

    private static string FormatDueDay(DateTimeOffset due)
    {
        var days = (due.Date - DateTime.Today).Days;
        return days switch
        {
            0  => "heute",
            1  => "morgen",
            -1 => "gestern",
            _  => due.ToString("ddd d. MMM", DeCh),
        };
    }

    private static string FormatCountdown(DateTimeOffset start)
    {
        var days = (start.Date - DateTime.Today).Days;
        return days switch
        {
            <= 0 => "heute",
            1    => "morgen",
            _    => $"in {days} Tagen"
        };
    }

    private static string FormatAgo(TimeSpan ts)
    {
        if (ts.TotalMinutes < 2)  return "gerade eben";
        if (ts.TotalMinutes < 60) return $"vor {(int)ts.TotalMinutes} Min.";
        if (ts.TotalHours   < 24) return $"vor {(int)ts.TotalHours} Std.";
        if (ts.TotalDays    < 2)  return "vor 1 Tag";
        return $"vor {(int)ts.TotalDays} Tagen";
    }

    // ── The log panel ───────────────────────────────────────────────────

    private void AppendLog(string line)
    {
        var current = TxtLog.Text;
        var isInit  = current.StartsWith("Bereit") || current.StartsWith("👁") || current.StartsWith("⏳");
        TxtLog.Text     = isInit ? line : current + "\n" + line;
        TxtLogLast.Text = line;
        LogScroll.ScrollToBottom();
    }
}
