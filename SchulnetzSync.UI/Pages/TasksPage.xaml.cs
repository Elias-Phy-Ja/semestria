using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchulnetzSync.Core.Tasks;
using SchulnetzSync.UI.Model;
// WinForms ist wegen NotifyIcon aktiviert und kollidiert bei vielen Steuerelementnamen
using Brushes        = System.Windows.Media.Brushes;
using Button         = System.Windows.Controls.Button;
using CheckBox       = System.Windows.Controls.CheckBox;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox       = System.Windows.Controls.ComboBox;
using Cursors        = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs   = System.Windows.Input.KeyEventArgs;
using Orientation    = System.Windows.Controls.Orientation;
using RadioButton    = System.Windows.Controls.RadioButton;

namespace SchulnetzSync.UI.Pages;

public partial class TasksPage : Page
{
    private static readonly CultureInfo DeCh = new("de-CH");

    /// <summary>Pseudo-Listen in der Leiste, die keine echte Liste sind.</summary>
    private const string ViewAll       = " ALL";
    private const string ViewImportant = " IMPORTANT";

    /// <summary>Angebotene Uhrzeiten in den beiden Zeit-Feldern.</summary>
    private static readonly string[] TimeSuggestions =
        ["07:30", "08:00", "09:00", "10:00", "12:00", "13:30", "16:00", "18:00", "20:00", "23:59"];

    /// <summary>Abgabe ohne Uhrzeit: Ende des Tages, sonst wäre sie ab 00:00 überfällig.</summary>
    private static readonly TimeSpan DefaultDueTime = new(23, 59, 0);

    /// <summary>
    /// Erinnerung ohne Uhrzeit: früher Abend. Mitternacht wäre wertlos — dann
    /// schläft man oder hat den Tag schon abgeschlossen.
    /// </summary>
    private static readonly TimeSpan DefaultReminderTime = new(18, 0, 0);

    private static readonly Color ImportantColor = Color.FromRgb(0xF5, 0x9E, 0x0B);
    private static readonly Color OverdueColor   = Color.FromRgb(0xEF, 0x44, 0x44);
    private static readonly Color NeutralColor   = Color.FromRgb(0x6B, 0x72, 0x80);

    /// <summary>Aktuell bearbeitete Aufgabe; null = neue Aufgabe.</summary>
    private TaskItem? _editing;

    /// <summary>"Open" | "Done" | "All"</summary>
    private string _filter = "Open";

    /// <summary>Gewählte Liste, oder <see cref="ViewAll"/> / <see cref="ViewImportant"/>.</summary>
    private string _selectedList = ViewAll;

    /// <summary>
    /// Ereignisse aus dem XAML feuern bereits während InitializeComponent() —
    /// bis dahin existieren die weiter unten deklarierten Elemente nicht.
    /// </summary>
    private bool _initialized;

    public TasksPage()
    {
        InitializeComponent();
        _initialized = true;

        CmbDueTime.ItemsSource    = TimeSuggestions;
        CmbRemindTime.ItemsSource = TimeSuggestions;
        RbOpen.IsChecked          = true;

        Loaded   += (_, _) => { AppState.Changed += OnStateChanged; Refresh(); };
        Unloaded += (_, _) => AppState.Changed -= OnStateChanged;
    }

    private void OnStateChanged() => Dispatcher.Invoke(Refresh);

    // ══════════════════════════════════════════════════════════════════════
    // Farben
    // ══════════════════════════════════════════════════════════════════════

    private Color AccentColor
        => TryFindResource("AccentColor") is Color c ? c : Color.FromRgb(0x5C, 0x6E, 0xF7);

    /// <summary>Farbe eines Eintrags der Leiste, echte Liste oder Pseudo-Liste.</summary>
    private Color ColorOf(string key) => key switch
    {
        ViewAll         => AccentColor,
        ViewImportant   => ImportantColor,
        TaskItem.NoList => NeutralColor,
        _               => ParseColor(AppState.TaskListColor(key)),
    };

    private Color ParseColor(string hex)
    {
        try   { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return AccentColor; }
    }

    private static SolidColorBrush Tint(Color c, byte alpha)
        => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    private Brush Resource(string key) => (Brush)FindResource(key);

    // ══════════════════════════════════════════════════════════════════════
    // Listen-Leiste
    // ══════════════════════════════════════════════════════════════════════

    private void BuildRail(IReadOnlyList<TaskItem> all)
    {
        ListRail.Children.Clear();

        ListRail.Children.Add(RailItem("Alle",    ViewAll,       "☰", all.Count(t => !t.IsDone)));
        ListRail.Children.Add(RailItem("Wichtig", ViewImportant, "★", all.Count(t => !t.IsDone && t.IsImportant)));

        var lists = AppState.TaskLists();
        if (lists.Count > 0)
            ListRail.Children.Add(new Border { Height = 12 });

        foreach (var name in lists)
        {
            int open = all.Count(t => !t.IsDone
                && string.Equals(t.ListName, name, StringComparison.OrdinalIgnoreCase));
            ListRail.Children.Add(RailItem(name, name, null, open));
        }

        // «Ohne Liste» nur zeigen, wenn es solche Aufgaben gibt
        int orphans = all.Count(t => !t.IsDone && string.IsNullOrWhiteSpace(t.ListName));
        if (orphans > 0 || _selectedList == TaskItem.NoList)
            ListRail.Children.Add(RailItem(TaskItem.NoList, TaskItem.NoList, null, orphans));
    }

    /// <param name="glyph">Zeichen für Pseudo-Listen; null zeichnet ein Farbfeld.</param>
    private UIElement RailItem(string label, string key, string? glyph, int count)
    {
        bool selected = _selectedList == key;
        var  color    = ColorOf(key);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement icon = glyph is not null
            ? new TextBlock
            {
                Text                = glyph,
                FontSize            = 15,
                Foreground          = new SolidColorBrush(color),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin              = new Thickness(1, 0, 0, 0),
            }
            : new Border
            {
                Background          = new SolidColorBrush(color),
                Width               = 13,
                Height              = 13,
                CornerRadius        = new CornerRadius(4),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin              = new Thickness(2, 0, 0, 0),
            };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text              = label,
            FontSize          = 13.5,
            FontWeight        = selected ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (count > 0)
        {
            var pill = CountPill(count);
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
        }

        var item = new Border
        {
            Child        = grid,
            Padding      = new Thickness(10, 9, 10, 9),
            CornerRadius = new CornerRadius(8),
            Margin       = new Thickness(0, 0, 0, 2),
            Cursor       = Cursors.Hand,
            Background   = selected ? Tint(color, 0x33) : Brushes.Transparent,
        };

        if (!selected)
        {
            item.MouseEnter += (_, _) => item.Background = Resource("HoverOverlay");
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        }

        item.MouseLeftButtonUp += (_, _) =>
        {
            _selectedList = key;
            CloseEditor();
            Refresh();
        };

        return item;
    }

    private UIElement CountPill(int count) => new Border
    {
        Background        = Resource("SubtleFill"),
        CornerRadius      = new CornerRadius(9),
        Padding           = new Thickness(7, 1, 7, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Margin            = new Thickness(8, 0, 0, 0),
        Child             = new TextBlock { Text = count.ToString(), FontSize = 11.5, Opacity = 0.75 },
    };

    private void BtnNewList_Click(object sender, RoutedEventArgs e)
    {
        bool show = NewListBox.Visibility != Visibility.Visible;
        NewListBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        TxtNewList.Text = "";
        BuildListSuggestions();
        TxtNewList.Focus();
    }

    /// <summary>Bietet Fachkürzel aus dem Stundenplan als Listennamen an.</summary>
    private void BuildListSuggestions()
    {
        var panel       = new StackPanel();
        var suggestions = AppState.SuggestedListNames().Take(8).ToList();

        if (suggestions.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text     = "Aus deinem Stundenplan:",
                FontSize = 11,
                Opacity  = 0.55,
                Margin   = new Thickness(2, 0, 0, 6),
            });

            var wrap = new WrapPanel();
            foreach (var name in suggestions)
            {
                var chip = new Button
                {
                    Content = name,
                    Style   = (Style)FindResource("ChipButton"),
                    Margin  = new Thickness(0, 0, 5, 5),
                };
                chip.Click += (_, _) => CreateList(name);
                wrap.Children.Add(chip);
            }
            panel.Children.Add(wrap);
        }

        ListSuggestions.Content = panel;
    }

    private void TxtNewList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { NewListBox.Visibility = Visibility.Collapsed; return; }
        if (e.Key != Key.Enter) return;

        var name = TxtNewList.Text.Trim();
        if (name.Length > 0) CreateList(name);
    }

    private void CreateList(string name)
    {
        AppState.AddTaskList(name);
        _selectedList         = name;
        TxtNewList.Text       = "";
        NewListBox.Visibility = Visibility.Collapsed;
        Refresh();
    }

    // ── Listenoptionen ───────────────────────────────────────────────────────

    private void BtnListOptions_Click(object sender, RoutedEventArgs e)
    {
        if (IsPseudoList(_selectedList)) return;
        BuildColorSwatches();

        // Erst nach dem Klick öffnen: Der Button hält die Maus noch, während
        // Click läuft. Ein sofort geöffnetes Popup mit StaysOpen=False deutet
        // das Loslassen als Klick daneben und schliesst sich gleich wieder.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
            () => ListOptionsPopup.IsOpen = true);
    }

    private void BuildColorSwatches()
    {
        ColorSwatches.Children.Clear();
        var current = AppState.TaskListColor(_selectedList);

        foreach (var (hex, name) in ColorPalette.All)
        {
            bool active = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);

            var swatch = new Border
            {
                Width        = 28,
                Height       = 28,
                CornerRadius = new CornerRadius(14),
                Margin       = new Thickness(0, 0, 6, 6),
                Background   = new SolidColorBrush(ParseColor(hex)),
                Cursor       = Cursors.Hand,
                ToolTip      = name,
                Child        = active
                    ? new TextBlock
                    {
                        Text                = "✓",
                        Foreground          = Brushes.White,
                        FontWeight          = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                    }
                    : null,
            };

            swatch.MouseLeftButtonUp += (_, _) =>
            {
                ListOptionsPopup.IsOpen = false;
                AppState.SetTaskListColor(_selectedList, hex);
            };

            ColorSwatches.Children.Add(swatch);
        }
    }

    private void BtnDeleteList_Click(object sender, RoutedEventArgs e)
    {
        ListOptionsPopup.IsOpen = false;
        if (IsPseudoList(_selectedList)) return;

        int inList = AppState.Tasks.Count(t =>
            string.Equals(t.ListName, _selectedList, StringComparison.OrdinalIgnoreCase));

        var confirm = MessageBox.Show(
            inList == 0
                ? $"Liste «{_selectedList}» löschen?"
                : $"Liste «{_selectedList}» löschen?\n\n"
                  + $"Die {inList} Aufgaben darin bleiben erhalten und landen unter «Ohne Liste».",
            "Liste löschen",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes) return;

        var removed = _selectedList;
        _selectedList = ViewAll;
        AppState.RemoveTaskList(removed);
    }

    private static bool IsPseudoList(string key)
        => key is ViewAll or ViewImportant or TaskItem.NoList;

    // ══════════════════════════════════════════════════════════════════════
    // Schnelleingabe
    // ══════════════════════════════════════════════════════════════════════

    private void TxtQuickAdd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { TxtQuickAdd.Text = ""; return; }
        if (e.Key != Key.Enter) return;

        var title = TxtQuickAdd.Text.Trim();
        if (title.Length == 0) return;

        // Wer in «Erledigt» tippt, soll die neue Aufgabe trotzdem sehen.
        if (_filter == "Done") RbOpen.IsChecked = true;

        AppState.AddTask(new TaskItem(
            Id:            Guid.NewGuid(),
            Title:         title,
            ListName:      IsPseudoList(_selectedList) ? "" : _selectedList,
            Notes:         null,
            DueAt:         null,
            RemindAt:      null,
            ReminderShown: false,
            IsImportant:   _selectedList == ViewImportant,
            IsDone:        false,
            CreatedAt:     DateTimeOffset.Now,
            CompletedAt:   null));

        TxtQuickAdd.Text = "";
        e.Handled = true;
    }

    private void BtnQuickDetails_Click(object sender, RoutedEventArgs e)
    {
        var title = TxtQuickAdd.Text.Trim();
        TxtQuickAdd.Text = "";
        OpenEditor(null, title);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Formular
    // ══════════════════════════════════════════════════════════════════════

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void OpenEditor(TaskItem? task, string? prefillTitle = null)
    {
        _editing = task;

        TxtEditorTitle.Text      = task is null ? "NEUE AUFGABE" : "AUFGABE BEARBEITEN";
        TxtEditorError.Text      = "";
        BtnEditorDelete.Visibility = task is null ? Visibility.Collapsed : Visibility.Visible;
        CmbList.ItemsSource      = AppState.TaskLists();
        TxtTitle.Text            = task?.Title ?? prefillTitle ?? "";
        TxtNotes.Text            = task?.Notes ?? "";
        ChkImportant.IsChecked   = task?.IsImportant ?? (_selectedList == ViewImportant);

        // Neue Aufgaben landen in der Liste, die gerade offen ist.
        CmbList.Text = task?.ListName
            ?? (IsPseudoList(_selectedList) ? "" : _selectedList);

        SetDateTime(DateDue,    CmbDueTime,    task?.DueAt);
        SetDateTime(DateRemind, CmbRemindTime, task?.RemindAt);

        EditorCard.Visibility = Visibility.Visible;
        TxtTitle.Focus();
        TxtTitle.CaretIndex = TxtTitle.Text.Length;
    }

    private void CloseEditor()
    {
        _editing = null;
        if (!_initialized) return;
        EditorCard.Visibility = Visibility.Collapsed;
        TxtEditorError.Text   = "";
    }

    private void TxtTitle_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  BtnSave_Click(sender, e);
        if (e.Key == Key.Escape) CloseEditor();
    }

    private void BtnEditorDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        if (ConfirmDelete(_editing)) CloseEditor();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var title = TxtTitle.Text.Trim();
        if (title.Length == 0)
        {
            TxtEditorError.Text = "Bitte gib an, was zu tun ist.";
            TxtTitle.Focus();
            return;
        }

        if (!TryReadDateTime(DateDue, CmbDueTime, DefaultDueTime, "Abgabe", out var due, out var dueError))
        {
            TxtEditorError.Text = dueError;
            return;
        }

        if (!TryReadDateTime(DateRemind, CmbRemindTime, DefaultReminderTime, "Erinnerung",
                             out var remind, out var remindError))
        {
            TxtEditorError.Text = remindError;
            return;
        }

        if (remind.HasValue && due.HasValue && remind.Value > due.Value)
        {
            TxtEditorError.Text = "Die Erinnerung liegt nach der Abgabe.";
            return;
        }

        // Eine neu gesetzte Erinnerung in der Vergangenheit würde sofort auslösen.
        // Eine unveränderte, bereits gezeigte beim Bearbeiten ist dagegen in Ordnung.
        bool reminderChanged = _editing is null || remind != _editing.RemindAt;
        if (reminderChanged && remind.HasValue && remind.Value < DateTimeOffset.Now)
        {
            TxtEditorError.Text = "Die Erinnerung liegt in der Vergangenheit.";
            return;
        }

        var listName  = CmbList.Text.Trim();
        var notes     = TxtNotes.Text.Trim();
        var important = ChkImportant.IsChecked == true;

        // Ein frei eingetippter Listenname wird zur echten Liste.
        if (listName.Length > 0) AppState.AddTaskList(listName);

        if (_editing is null)
        {
            AppState.AddTask(new TaskItem(
                Id:            Guid.NewGuid(),
                Title:         title,
                ListName:      listName,
                Notes:         notes.Length == 0 ? null : notes,
                DueAt:         due,
                RemindAt:      remind,
                ReminderShown: false,
                IsImportant:   important,
                IsDone:        false,
                CreatedAt:     DateTimeOffset.Now,
                CompletedAt:   null));
        }
        else
        {
            AppState.UpdateTask(_editing with
            {
                Title         = title,
                ListName      = listName,
                Notes         = notes.Length == 0 ? null : notes,
                DueAt         = due,
                RemindAt      = remind,
                IsImportant   = important,
                // Verschobene Erinnerung soll erneut ausgelöst werden
                ReminderShown = reminderChanged ? false : _editing.ReminderShown,
            });
        }

        CloseEditor();
    }

    /// <summary>Setzt die Erinnerung relativ zur Abgabe (Tage im Tag-Attribut, 0 = keine).</summary>
    private void BtnRemindPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        if (!int.TryParse(tag, out int daysBefore)) return;

        if (daysBefore == 0)
        {
            DateRemind.SelectedDate = null;
            CmbRemindTime.Text      = "";
            return;
        }

        if (!TryReadDateTime(DateDue, CmbDueTime, DefaultDueTime, "Abgabe", out var due, out var error))
        {
            TxtEditorError.Text = error;
            return;
        }
        if (due is null)
        {
            TxtEditorError.Text = "Setze zuerst einen Abgabetermin.";
            return;
        }

        TxtEditorError.Text = "";

        // Tag vom Abgabetermin zurückrechnen, Uhrzeit aber auf den frühen Abend —
        // «am Vortag» einer Abgabe um 23:59 hiesse sonst: kurz vor Mitternacht.
        var day   = due.Value.Date.AddDays(-daysBefore) + DefaultReminderTime;
        var local = new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
        SetDateTime(DateRemind, CmbRemindTime, local);
    }

    /// <summary>Ohne Uhrzeit ist die Abgabe das Tagesende — sonst wäre sie um 00:00 sofort überfällig.</summary>
    private void DateDue_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (DateDue.SelectedDate.HasValue && CmbDueTime.Text.Trim().Length == 0)
            CmbDueTime.Text = "23:59";
    }

    private static void SetDateTime(DatePicker date, ComboBox time, DateTimeOffset? value)
    {
        if (value is null)
        {
            date.SelectedDate = null;
            time.Text         = "";
            return;
        }
        date.SelectedDate = value.Value.DateTime.Date;
        time.Text         = value.Value.ToString("HH:mm", DeCh);
    }

    /// <summary>
    /// Reads a date and time pair from the form.
    /// </summary>
    /// <param name="fallbackTime">Used when the time field is empty.</param>
    /// <param name="field">Name for the error message, e.g. "Abgabe".</param>
    /// <param name="value">Null when no date is set — that is allowed.</param>
    /// <returns>
    /// False only for unreadable input. Früher wurde eine unlesbare Uhrzeit still
    /// durch 23:59 ersetzt; jetzt erfährt man, dass etwas nicht stimmt.
    /// </returns>
    private static bool TryReadDateTime(
        DatePicker date, ComboBox time, TimeSpan fallbackTime, string field,
        out DateTimeOffset? value, out string? error)
    {
        value = null;
        error = null;

        if (date.SelectedDate is not { } day)
        {
            // Etwas getippt, das kein Datum ist? Nicht stillschweigend verwerfen.
            if (!string.IsNullOrWhiteSpace(date.Text))
            {
                error = $"{field}: «{date.Text.Trim()}» ist kein gültiges Datum.";
                return false;
            }
            return true;
        }

        var text = time.Text.Trim();
        TimeSpan clock;
        if (text.Length == 0)
            clock = fallbackTime;
        else if (!ClockTime.TryParse(text, out clock))
        {
            error = $"{field}: «{text}» ist keine gültige Uhrzeit. Zum Beispiel 8:30, 8.30 oder 0830.";
            return false;
        }

        var local = day.Date + clock;
        value = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Liste
    // ══════════════════════════════════════════════════════════════════════

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) _filter = tag;
        if (!_initialized) return;
        Refresh();
    }

    /// <summary>Entfernt erledigte Aufgaben — nur im gerade gezeigten Bereich.</summary>
    private void BtnClearDone_Click(object sender, RoutedEventArgs e)
    {
        var inScope = InScope();
        int done    = AppState.Tasks.Count(t => t.IsDone && inScope(t));
        if (done == 0) return;

        var where = IsPseudoList(_selectedList) && _selectedList != TaskItem.NoList
            ? (_selectedList == ViewImportant ? " unter «Wichtig»" : "")
            : $" in «{_selectedList}»";

        var confirm = MessageBox.Show(
            $"{done} erledigte Aufgaben{where} endgültig entfernen?",
            "Erledigte entfernen",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (confirm == MessageBoxResult.Yes) AppState.ClearCompletedTasks(inScope);
    }

    /// <summary>Welche Aufgaben zur Auswahl in der Leiste gehören.</summary>
    private Func<TaskItem, bool> InScope()
    {
        var list = _selectedList;
        return list switch
        {
            ViewAll         => _ => true,
            ViewImportant   => t => t.IsImportant,
            TaskItem.NoList => t => string.IsNullOrWhiteSpace(t.ListName),
            _               => t => string.Equals(t.ListName, list, StringComparison.OrdinalIgnoreCase),
        };
    }

    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        var all = AppState.Tasks;

        BuildRail(all);
        UpdateHeader();

        var scoped = all.Where(InScope()).ToList();

        int open    = scoped.Count(t => !t.IsDone);
        int done    = scoped.Count - open;
        int overdue = scoped.Count(t => t.IsOverdue(now));

        var parts = new List<string> { open == 1 ? "1 offen" : $"{open} offen" };
        if (overdue > 0) parts.Add($"{overdue} überfällig");
        if (done > 0)    parts.Add($"{done} erledigt");
        TxtSummary.Text = scoped.Count == 0 ? "Noch keine Aufgaben" : string.Join("  ·  ", parts);

        BtnClearDone.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;

        var items = (_filter switch
        {
            "Done" => scoped.Where(t => t.IsDone),
            "All"  => scoped,
            _      => scoped.Where(t => !t.IsDone),
        }).ToList();

        TaskList.Children.Clear();

        if (items.Count == 0)
        {
            ShowEmptyState(scoped);
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;

        // In einer einzelnen Liste braucht es keine Gruppenköpfe.
        if (_selectedList is not (ViewAll or ViewImportant))
        {
            var color = ColorOf(_selectedList);
            foreach (var task in Sort(items))
                TaskList.Children.Add(BuildRow(task, color, now));
            return;
        }

        var groups = items
            .GroupBy(t => t.ListLabel)
            .OrderBy(g => g.Key == TaskItem.NoList ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.CurrentCulture);

        foreach (var group in groups)
            TaskList.Children.Add(BuildGroup(group.Key, group, now));
    }

    private void UpdateHeader()
    {
        var color = ColorOf(_selectedList);
        HeaderSwatch.Background = new SolidColorBrush(color);

        (TxtPageTitle.Text, HeaderGlyph.Text) = _selectedList switch
        {
            ViewAll         => ("Alle Aufgaben", "☰"),
            ViewImportant   => ("Wichtig", "★"),
            TaskItem.NoList => (TaskItem.NoList, "–"),
            _               => (_selectedList, _selectedList[..1].ToUpper(DeCh)),
        };

        BtnListOptions.Visibility = IsPseudoList(_selectedList)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowEmptyState(IReadOnlyList<TaskItem> scoped)
    {
        EmptyState.Visibility = Visibility.Visible;

        (TxtEmptyGlyph.Text, TxtEmptyTitle.Text, TxtEmptyBody.Text) = (_filter, _selectedList) switch
        {
            ("Done", _) =>
                ("✓", "Noch nichts erledigt", "Abgehakte Aufgaben landen hier."),
            (_, ViewImportant) =>
                ("★", "Nichts als wichtig markiert", "Markiere Aufgaben mit dem Stern, dann erscheinen sie hier."),
            ("Open", _) when scoped.Count > 0 =>
                ("🎉", "Alles erledigt", "Stark. Neue Aufgaben tippst du oben ein."),
            _ =>
                ("📝", "Noch keine Aufgaben", "Tipp oben eine Aufgabe ein und drück Enter."),
        };
    }

    /// <summary>Wichtiges zuerst, dann nach Abgabe, Erledigtes nach unten.</summary>
    private static IEnumerable<TaskItem> Sort(IEnumerable<TaskItem> tasks)
        => tasks
            .OrderBy(t => t.IsDone)
            .ThenByDescending(t => t.IsImportant)
            .ThenBy(t => t.DueAt ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.CreatedAt);

    private UIElement BuildGroup(string list, IEnumerable<TaskItem> tasks, DateTimeOffset now)
    {
        var color   = ColorOf(list);
        var ordered = Sort(tasks).ToList();
        var panel   = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(2, 0, 0, 8),
        };
        header.Children.Add(new Border
        {
            Background        = new SolidColorBrush(color),
            Width             = 11,
            Height            = 11,
            CornerRadius      = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 9, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text              = list,
            FontSize          = 13.5,
            FontWeight        = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(CountPill(ordered.Count));
        panel.Children.Add(header);

        foreach (var task in ordered)
            panel.Children.Add(BuildRow(task, color, now));

        return panel;
    }

    private UIElement BuildRow(TaskItem task, Color listColor, DateTimeOffset now)
    {
        bool overdue = task.IsOverdue(now);

        var card = new Border
        {
            CornerRadius    = new CornerRadius(10),
            Margin          = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            BorderBrush     = overdue ? Tint(OverdueColor, 0x70) : Brushes.Transparent,
        };

        // Theme-Brushes von ModernWpf lassen sich nicht statisch auflösen —
        // FindResource wirft dort. Die Referenz folgt zudem einem Themewechsel.
        card.SetResourceReference(Border.BackgroundProperty,
            "SystemControlBackgroundChromeMediumLowBrush");

        // Innere Fläche für den Hover-Effekt über dem Theme-Hintergrund
        var surface = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding      = new Thickness(14, 11, 8, 11),
            Background   = Brushes.Transparent,
            Cursor       = Cursors.Hand,
        };
        card.Child = surface;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        surface.Child = grid;

        // ── Abhakkreis ──
        var check = new CheckBox
        {
            Style             = (Style)FindResource("RoundCheck"),
            BorderBrush       = new SolidColorBrush(listColor),
            IsChecked         = task.IsDone,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 14, 0),
            Tag               = task,
            ToolTip           = task.IsDone ? "Wieder öffnen" : "Erledigt",
        };
        check.Checked   += TaskCheck_Changed;
        check.Unchecked += TaskCheck_Changed;
        Grid.SetColumn(check, 0);
        grid.Children.Add(check);

        // ── Inhalt ──
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        body.Children.Add(new TextBlock
        {
            Text            = task.Title,
            FontSize        = 14.5,
            FontWeight      = FontWeights.SemiBold,
            TextWrapping    = TextWrapping.Wrap,
            TextDecorations = task.IsDone ? TextDecorations.Strikethrough : null,
            Opacity         = task.IsDone ? 0.55 : 1.0,
        });

        var chips = BuildChips(task, now);
        if (chips.Children.Count > 0) body.Children.Add(chips);

        if (!string.IsNullOrWhiteSpace(task.Notes))
            body.Children.Add(new TextBlock
            {
                Text         = task.Notes.ReplaceLineEndings(" "),
                FontSize     = 12,
                Opacity      = 0.55,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0, 5, 0, 0),
                ToolTip      = task.Notes,
            });

        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        // ── Aktionen, erst beim Überfahren sichtbar ──
        var actions = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility        = Visibility.Hidden,
            Margin            = new Thickness(8, 0, 0, 0),
        };
        actions.Children.Add(IconButton("✎", "Bearbeiten", (_, _) => OpenEditor(task)));
        actions.Children.Add(IconButton("🗑", "Löschen",    (_, _) => ConfirmDelete(task)));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        // ── Stern ──
        var star = IconButton(
            task.IsImportant ? "★" : "☆",
            task.IsImportant ? "Nicht mehr wichtig" : "Als wichtig markieren",
            (_, _) => AppState.UpdateTask(task with { IsImportant = !task.IsImportant }));
        star.FontSize   = 18;
        star.Foreground = task.IsImportant
            ? new SolidColorBrush(ImportantColor)
            : Tint(NeutralColor, 0xCC);
        Grid.SetColumn(star, 3);
        grid.Children.Add(star);

        surface.MouseEnter += (_, _) =>
        {
            surface.Background = Resource("HoverOverlay");
            actions.Visibility = Visibility.Visible;
        };
        surface.MouseLeave += (_, _) =>
        {
            surface.Background = Brushes.Transparent;
            actions.Visibility = Visibility.Hidden;
        };

        // Klick auf die Zeile öffnet sie. Kreis und Knöpfe fangen ihre Klicks
        // selbst ab, lösen das hier also nicht aus.
        surface.MouseLeftButtonUp += (_, _) => OpenEditor(task);

        return card;
    }

    private Button IconButton(string glyph, string tooltip, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = glyph,
            Style   = (Style)FindResource("GhostButton"),
            Padding = new Thickness(8, 2, 8, 4),
            FontSize = 14,
            ToolTip = tooltip,
        };
        button.Click += onClick;
        return button;
    }

    /// <summary>Kleine Etiketten für Abgabe und Erinnerung.</summary>
    private WrapPanel BuildChips(TaskItem task, DateTimeOffset now)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };

        if (task.DueAt is { } due)
        {
            var time = due.TimeOfDay == DefaultDueTime ? "" : $", {due:HH\\:mm}";
            var text = FormatDay(due) + time;

            if (task.IsDone)
                panel.Children.Add(Chip("📅 " + text, null));
            else if (due < now)
                panel.Children.Add(Chip("⚠ Überfällig · " + text, OverdueColor));
            else if (due.Date <= now.Date.AddDays(1))
                panel.Children.Add(Chip("📅 " + text, ImportantColor));
            else
                panel.Children.Add(Chip("📅 " + text, null));
        }

        if (!task.IsDone && task.RemindAt is { } remind && remind > now)
            panel.Children.Add(Chip($"🔔 {FormatDay(remind)}, {remind:HH\\:mm}", null));

        return panel;
    }

    /// <param name="tint">Farbe für Hervorhebung; null für ein neutrales Etikett.</param>
    private UIElement Chip(string text, Color? tint)
    {
        var label = new TextBlock { Text = text, FontSize = 11.5 };

        if (tint is { } c)
            label.Foreground = new SolidColorBrush(c);
        else
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, "SystemControlForegroundBaseHighBrush");
            label.Opacity = 0.75;
        }

        return new Border
        {
            Background   = tint is { } t ? Tint(t, 0x26) : Resource("SubtleFill"),
            CornerRadius = new CornerRadius(6),
            Padding      = new Thickness(7, 2, 7, 3),
            Margin       = new Thickness(0, 0, 6, 0),
            Child        = label,
        };
    }

    private void TaskCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: TaskItem task } box) return;
        bool done = box.IsChecked == true;
        if (done == task.IsDone) return;

        AppState.UpdateTask(task with
        {
            IsDone      = done,
            CompletedAt = done ? DateTimeOffset.Now : null,
        });
    }

    /// <returns>True when the task was deleted.</returns>
    private static bool ConfirmDelete(TaskItem task)
    {
        var confirm = MessageBox.Show(
            $"«{task.Title}» löschen?",
            "Aufgabe löschen",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes) return false;
        AppState.RemoveTask(task.Id);
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Darstellung
    // ══════════════════════════════════════════════════════════════════════

    private static string FormatDay(DateTimeOffset value)
    {
        int days = (value.Date - DateTime.Today).Days;
        return days switch
        {
            0  => "Heute",
            1  => "Morgen",
            -1 => "Gestern",
            > 1 and < 7 => value.ToString("dddd", DeCh),
            _  => value.ToString("ddd, d. MMM", DeCh),
        };
    }
}
