using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Model;
using SchulnetzSync.UI.Services;

namespace SchulnetzSync.UI.Pages;

public partial class DashboardPage : Page
{
    private static readonly CultureInfo DeCh = new("de-CH");

    /// <summary>Zeitfenster der Vorschau unten auf dem Dashboard.</summary>
    private const int UpcomingDays = 7;

    private readonly SyncService _sync = new();
    private CancellationTokenSource? _cts;

    /// <summary>Merkt den letzten Ladezustand, damit der Start-Refresh nur einmal geloggt wird.</summary>
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

        // AppState.Changed feuert vom Hintergrund-Thread → immer dispatchen
        Loaded   += (_, _) => { AppState.Changed += OnStateChanged; RefreshStatus(); };
        Unloaded += (_, _) => AppState.Changed -= OnStateChanged;
    }

    private void OnStateChanged() => Dispatcher.Invoke(RefreshStatus);

    // -----------------------------------------------------------------------
    // Buttons
    // -----------------------------------------------------------------------

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
        => await StartSyncAsync(dryRun: false);

    private async void BtnDryRun_Click(object sender, RoutedEventArgs e)
        => await StartSyncAsync(dryRun: true);

    private void BtnGoSettings_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main)
            main.NavigateTo("Settings");
    }

    private void BtnGoEvents_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main)
            main.NavigateTo("Events");
    }

    // -----------------------------------------------------------------------
    // Sync
    // -----------------------------------------------------------------------

    private async Task StartSyncAsync(bool dryRun)
    {
        if (AppState.IsSyncing || AppState.IsRefreshingFeed) return;
        TxtLog.Text = dryRun ? "👁  Feed wird geladen…" : "⏳ Synchronisation startet…";
        _cts = new CancellationTokenSource();
        await _sync.RunAsync(dryRun, _cts.Token);
    }

    private void OnSyncCompleted(SyncResult result) => RefreshStatus();

    // -----------------------------------------------------------------------
    // Status
    // -----------------------------------------------------------------------

    private void RefreshStatus()
    {
        var config      = AppState.Config;
        bool hasFeed    = ConfigManager.GetFeedUrl(config) is not null;
        bool hasOutlook = MicrosoftAccount.IsAvailable(config);
        bool busy       = AppState.IsSyncing || AppState.IsRefreshingFeed;

        LogAutoRefreshTransition(config);

        SetupHint.Visibility = hasFeed ? Visibility.Collapsed : Visibility.Visible;

        // Outlook-Sync-Button nur zeigen wenn Microsoft-Konto konfiguriert
        BtnSync.Visibility  = hasOutlook ? Visibility.Visible : Visibility.Collapsed;
        BtnSync.IsEnabled   = hasFeed && hasOutlook && !busy;
        BtnDryRun.IsEnabled = hasFeed && !busy;

        // Ladesymbol
        SyncRing.IsActive      = busy;
        TxtBusyHint.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TxtBusyHint.Text       = AppState.IsSyncing ? "Sync läuft…" : "Feed wird geladen…";

        RefreshStatusLines(config, busy);
        RefreshStats();
        BuildUpcoming(config);

        TxtHeaderSub.Text = hasFeed
            ? "Alles im Blick."
            : "Fast fertig. Feed-URL in den Einstellungen eintragen.";
    }

    /// <summary>
    /// Der Auto-Refresh beim Start läuft still im Hintergrund. Damit der Benutzer
    /// sieht, dass überhaupt etwas passiert, wird der Zustandswechsel protokolliert.
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
                    : "⚠️ Feed nicht erreichbar — zeige zwischengespeicherte Daten.");
        }
        _wasRefreshingFeed = AppState.IsRefreshingFeed;
    }

    /// <summary>Statuspunkt, Feed-Zeitstempel und Outlook-Zeitstempel.</summary>
    private void RefreshStatusLines(SyncConfig config, bool busy)
    {
        TxtLastFeed.Text = config.LastFeedRefreshAt.HasValue
            ? $"Feed aktualisiert: {FormatAgo(DateTimeOffset.Now - config.LastFeedRefreshAt.Value)}"
            : "Feed: noch nie geladen";

        TxtLastRun.Text = config.LastRunAt.HasValue
            ? $"Outlook-Sync: {FormatAgo(DateTimeOffset.Now - config.LastRunAt.Value)}"
            : "Outlook-Sync: noch nie ausgeführt";

        TxtLastResult.Text = config.LastRunResult ?? "";

        if (busy)
        {
            StatusDot.Fill      = Brush(0x5C, 0x6E, 0xF7);
            TxtStatusLabel.Text = AppState.IsSyncing ? "Synchronisation läuft" : "Feed wird geladen";
            TxtStatStatus.Text  = "⏳";
            TxtStatDetail.Text  = "Läuft…";
            return;
        }

        // Der Feed-Zeitstempel ist der aussagekräftigere: er wird bei jedem Start erneuert
        var reference = config.LastFeedRefreshAt ?? config.LastRunAt;
        if (reference.HasValue)
        {
            var ago    = DateTimeOffset.Now - reference.Value;
            bool fresh = ago.TotalHours < 25;
            StatusDot.Fill      = fresh ? Brush(0x22, 0xC5, 0x5E) : Brush(0xF5, 0x9E, 0x0B);
            TxtStatusLabel.Text = fresh ? "Daten sind aktuell" : "Aktualisierung fällig";
            TxtStatStatus.Text  = fresh ? "✅" : "⚠️";
            TxtStatDetail.Text  = FormatAgo(ago);
        }
        else
        {
            StatusDot.Fill      = Brush(0xF5, 0x9E, 0x0B);
            TxtStatusLabel.Text = "Noch kein Sync";
            TxtStatStatus.Text  = "⏳";
            TxtStatDetail.Text  = "Noch kein Lauf";
        }
    }

    private void RefreshStats()
    {
        var cached = AppState.CachedFeedEvents;
        if (cached.Count > 0)
        {
            TxtStatPruefung.Text = cached.Count(e => e.Type == SchulnetzEventType.Pruefung).ToString();
            TxtStatTermin.Text   = cached.Count(e => e.Type == SchulnetzEventType.Termin).ToString();
        }
        else
        {
            TxtStatPruefung.Text = "·";
            TxtStatTermin.Text   = "·";
        }
    }

    // -----------------------------------------------------------------------
    // Nächste 7 Tage
    // -----------------------------------------------------------------------

    /// <summary>
    /// Baut die Vorschau unten auf. Angezeigt wird genau das, was auch synchronisiert
    /// wird: Prüfungen und/oder Termine gemäss <see cref="SyncConfig.EnabledTypes"/>.
    /// Ist keiner der beiden Typen aktiviert, verschwindet die Sektion ganz.
    /// </summary>
    private void BuildUpcoming(SyncConfig config)
    {
        bool showPruefungen = config.EnabledTypes.Contains(SchulnetzEventType.Pruefung);
        bool showTermine    = config.EnabledTypes.Contains(SchulnetzEventType.Termin);

        if (!showPruefungen && !showTermine)
        {
            UpcomingCard.Visibility = Visibility.Collapsed;
            return;
        }

        UpcomingCard.Visibility = Visibility.Visible;
        TxtUpcomingTitle.Text = (showPruefungen, showTermine) switch
        {
            (true, true)  => $"PRÜFUNGEN & TERMINE · NÄCHSTE {UpcomingDays} TAGE",
            (true, false) => $"PRÜFUNGEN · NÄCHSTE {UpcomingDays} TAGE",
            _             => $"TERMINE · NÄCHSTE {UpcomingDays} TAGE"
        };

        var items = CollectUpcoming(showPruefungen, showTermine);

        UpcomingList.Children.Clear();
        if (items.Count == 0)
        {
            TxtUpcomingEmpty.Visibility = Visibility.Visible;
            TxtUpcomingEmpty.Text = AppState.CachedFeedEvents.Count == 0
                ? "Noch keine Daten geladen."
                : (showPruefungen, showTermine) switch
                {
                    (true, true)  => $"Keine Prüfungen und Termine in den nächsten {UpcomingDays} Tagen.",
                    (true, false) => $"Keine Prüfungen in den nächsten {UpcomingDays} Tagen.",
                    _             => $"Keine Termine in den nächsten {UpcomingDays} Tagen."
                };
            return;
        }

        TxtUpcomingEmpty.Visibility = Visibility.Collapsed;
        foreach (var ev in items)
            UpcomingList.Children.Add(BuildUpcomingRow(ev));
    }

    /// <summary>
    /// Sammelt Feed- und manuelle Events der aktivierten Typen im Zeitfenster
    /// «jetzt bis Ende des 7. Folgetags». Ausgeblendete Einträge bleiben draussen.
    /// </summary>
    private static List<SchulnetzEvent> CollectUpcoming(bool showPruefungen, bool showTermine)
    {
        var now        = DateTimeOffset.Now;
        var windowEnd  = new DateTimeOffset(DateTime.Today.AddDays(UpcomingDays + 1), now.Offset);
        var suppressed = AppState.SuppressedKeys;

        bool TypeWanted(SchulnetzEventType t)
            => (t == SchulnetzEventType.Pruefung && showPruefungen)
            || (t == SchulnetzEventType.Termin   && showTermine);

        return AppState.CachedFeedEvents.Concat(AppState.ManualAsEvents())
            .Where(e => TypeWanted(e.Type))
            .Where(e => !suppressed.Contains(e.Key))
            .Where(e => e.End >= now && e.Start < windowEnd)
            .OrderBy(e => e.Start)
            .ThenBy(e => e.Summary, StringComparer.CurrentCulture)
            .ToList();
    }

    private UIElement BuildUpcomingRow(SchulnetzEvent ev)
    {
        var color = ResolveColor(ev);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Farbiger Balken links
        var bar = new Border
        {
            Background   = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
            Margin       = new Thickness(0, 2, 12, 2)
        };
        Grid.SetColumn(bar, 0);
        grid.Children.Add(bar);

        // Datum + Zeit
        var when = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        when.Children.Add(new TextBlock
        {
            Text       = FormatDay(ev.Start),
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold
        });
        when.Children.Add(new TextBlock
        {
            Text     = ev.IsAllDay ? "ganztägig" : $"{ev.Start:HH:mm}–{ev.End:HH:mm}",
            FontSize = 12,
            Opacity  = 0.70
        });
        Grid.SetColumn(when, 1);
        grid.Children.Add(when);

        // Titel + Ort
        var what = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 8, 0)
        };
        what.Children.Add(new TextBlock
        {
            Text         = ev.Summary,
            FontSize     = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrWhiteSpace(ev.Location))
            what.Children.Add(new TextBlock
            {
                Text         = "📍 " + ev.Location,
                FontSize     = 12,
                Opacity      = 0.70,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        Grid.SetColumn(what, 2);
        grid.Children.Add(what);

        // Countdown-Chip rechts
        var chip = new Border
        {
            Background        = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B)),
            BorderBrush       = new SolidColorBrush(color),
            BorderThickness   = new Thickness(1),
            CornerRadius      = new CornerRadius(10),
            Padding           = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text       = FormatCountdown(ev.Start),
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold
            }
        };
        Grid.SetColumn(chip, 3);
        grid.Children.Add(chip);

        return grid;
    }

    /// <summary>Farbe aus den Benutzereinstellungen; fällt auf die Standardfarbe zurück.</summary>
    private static Color ResolveColor(SchulnetzEvent ev)
    {
        var key = ev.Type == SchulnetzEventType.Pruefung ? "Pruefung" : "Termin";
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(AppState.GetEventColor(key)); }
        catch
        {
            return ev.Type == SchulnetzEventType.Pruefung
                ? Color.FromRgb(0xDC, 0x26, 0x26)
                : Color.FromRgb(0xD9, 0x77, 0x06);
        }
    }

    private static string FormatDay(DateTimeOffset start)
    {
        var days = (start.Date - DateTime.Today).Days;
        return days switch
        {
            0 => "Heute",
            1 => "Morgen",
            _ => start.ToString("ddd, d. MMM", DeCh)
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

    // -----------------------------------------------------------------------
    // Kleinkram
    // -----------------------------------------------------------------------

    private void AppendLog(string line)
    {
        var current = TxtLog.Text;
        var isInit  = current.StartsWith("Bereit") || current.StartsWith("👁") || current.StartsWith("⏳");
        TxtLog.Text = isInit ? line : current + "\n" + line;
        LogScroll.ScrollToBottom();
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
        => new(Color.FromRgb(r, g, b));

    private static string FormatAgo(TimeSpan ts)
    {
        if (ts.TotalMinutes < 2)  return "gerade eben";
        if (ts.TotalMinutes < 60) return $"vor {(int)ts.TotalMinutes} Min.";
        if (ts.TotalHours   < 24) return $"vor {(int)ts.TotalHours} Std.";
        if (ts.TotalDays    < 2)  return "vor 1 Tag";
        return $"vor {(int)ts.TotalDays} Tagen";
    }
}
