using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchulnetzSync.Core.Tasks;
using SchulnetzSync.UI.Model;
// WinForms is in the build for NotifyIcon and clashes with half the control names
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

/// <summary>
/// Tasks: the list rail on the left, the tasks in the middle, the edit form on the right.
///
/// Nothing here ever reaches Outlook. Tasks live only in <see cref="AppState"/>, and the
/// page rebuilds its rows from there after every change instead of keeping its own copy.
/// </summary>
public partial class TasksPage : Page
{
    private static readonly CultureInfo DeCh = new("de-CH");

    /// <summary>Entries in the rail that are not really lists.</summary>
    private const string ViewAll       = " ALL";
    private const string ViewImportant = " IMPORTANT";

    /// <summary>The times offered in the two time fields.</summary>
    private static readonly string[] TimeSuggestions =
        ["07:30", "08:00", "09:00", "10:00", "12:00", "13:30", "16:00", "18:00", "20:00", "23:59"];

    /// <summary>A due date without a time means end of day — at 00:00 it would be overdue immediately.</summary>
    private static readonly TimeSpan DefaultDueTime = new(23, 59, 0);

    /// <summary>
    /// A reminder without a time lands in the early evening. Midnight would be useless:
    /// by then you are asleep or done with the day either way.
    /// </summary>
    private static readonly TimeSpan DefaultReminderTime = new(18, 0, 0);

    private static readonly Color ImportantColor = Color.FromRgb(0xF5, 0x9E, 0x0B);
    private static readonly Color OverdueColor   = Color.FromRgb(0xEF, 0x44, 0x44);
    private static readonly Color NeutralColor   = Color.FromRgb(0x6B, 0x72, 0x80);

    /// <summary>The task being edited; null means a new one.</summary>
    private TaskItem? _editing;

    /// <summary>"Open" | "Done" | "All"</summary>
    private string _filter = "Open";

    /// <summary>The selected list, or <see cref="ViewAll"/> / <see cref="ViewImportant"/>.</summary>
    private string _selectedList = ViewAll;

    /// <summary>
    /// XAML events already fire during InitializeComponent(), and the elements declared
    /// further down do not exist yet at that point. This flag keeps them quiet until they do.
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
    // Colours
    // ══════════════════════════════════════════════════════════════════════

    private Color AccentColor
        => TryFindResource("AccentColor") is Color c ? c : Color.FromRgb(0x5C, 0x6E, 0xF7);

    /// <summary>Colour of a rail entry, real list or not.</summary>
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
    // The list rail
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

        // Only show "Ohne Liste" when there actually are such tasks.
        int orphans = all.Count(t => !t.IsDone && string.IsNullOrWhiteSpace(t.ListName));
        if (orphans > 0 || _selectedList == TaskItem.NoList)
            ListRail.Children.Add(RailItem(TaskItem.NoList, TaskItem.NoList, null, orphans));
    }

    /// <param name="glyph">Icon for the pseudo lists; null draws a colour swatch instead.</param>
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

    /// <summary>Offers subject codes from the timetable as list names.</summary>
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

    // ── List options ─────────────────────────────────────────────────────────

    private void BtnListOptions_Click(object sender, RoutedEventArgs e)
    {
        if (IsPseudoList(_selectedList)) return;
        BuildColorSwatches();

        // Open after the click, not during it. The button still has the mouse captured
        // while Click runs, and a popup with StaysOpen=False reads the release as a click
        // outside itself and shuts again straight away.
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
    // Quick add
    // ══════════════════════════════════════════════════════════════════════

    private void TxtQuickAdd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { TxtQuickAdd.Text = ""; return; }
        if (e.Key != Key.Enter) return;

        var title = TxtQuickAdd.Text.Trim();
        if (title.Length == 0) return;

        // Typing while "Erledigt" is showing should still put the new task in front of you.
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
    // The edit form
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

        // A new task lands in whichever list is open right now.
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

        // A reminder newly set in the past would fire immediately. One that was already
        // shown and is left untouched while editing is fine, though.
        bool reminderChanged = _editing is null || remind != _editing.RemindAt;
        if (reminderChanged && remind.HasValue && remind.Value < DateTimeOffset.Now)
        {
            TxtEditorError.Text = "Die Erinnerung liegt in der Vergangenheit.";
            return;
        }

        var listName  = CmbList.Text.Trim();
        var notes     = TxtNotes.Text.Trim();
        var important = ChkImportant.IsChecked == true;

        // A list name typed by hand becomes a real list.
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
                // A reminder that was moved should be allowed to fire again.
                ReminderShown = reminderChanged ? false : _editing.ReminderShown,
            });
        }

        CloseEditor();
    }

    /// <summary>Sets the reminder relative to the due date; days come from the Tag attribute, 0 means none.</summary>
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

        // Count the days back from the due date, but put the time in the early evening:
        // "the day before" something due at 23:59 would otherwise mean just before midnight.
        var day   = due.Value.Date.AddDays(-daysBefore) + DefaultReminderTime;
        var local = new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
        SetDateTime(DateRemind, CmbRemindTime, local);
    }

    /// <summary>With no time given the due date is the end of the day; at 00:00 it would already be late.</summary>
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

    /// <summary>Reads a date and time pair out of the form.</summary>
    /// <param name="fallbackTime">Used when the time field is empty.</param>
    /// <param name="field">Name for the error message, e.g. "Abgabe".</param>
    /// <param name="value">Null when no date is set; that is allowed.</param>
    /// <returns>
    /// False only for input that cannot be read. This used to swallow an unreadable time
    /// and quietly substitute 23:59; now you get told that something is off.
    /// </returns>
    private static bool TryReadDateTime(
        DatePicker date, ComboBox time, TimeSpan fallbackTime, string field,
        out DateTimeOffset? value, out string? error)
    {
        value = null;
        error = null;

        if (date.SelectedDate is not { } day)
        {
            // Something typed that is not a date — say so rather than dropping it.
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
    // The task list itself
    // ══════════════════════════════════════════════════════════════════════

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) _filter = tag;
        if (!_initialized) return;
        Refresh();
    }

    /// <summary>Clears finished tasks, but only within the view that is open.</summary>
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

    /// <summary>Which tasks belong to the current selection in the rail.</summary>
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

        // Inside a single list there is nothing to group by.
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
            TaskItem.NoList => (TaskItem.NoList, "○"),
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

    /// <summary>Starred first, then by due date, finished ones at the bottom.</summary>
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

        // ModernWpf theme brushes cannot be resolved statically — FindResource throws on
        // them. A dynamic reference also follows along when the theme changes.
        card.SetResourceReference(Border.BackgroundProperty,
            "SystemControlBackgroundChromeMediumLowBrush");

        // Inner surface, so the hover effect sits on top of the theme background
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

        // ── Tick circle ──
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

        // ── Content ──
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

        // ── Actions, only visible on hover ──
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

        // ── Star ──
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

        // Clicking the row opens it. The circle and the buttons handle their own clicks,
        // so they never get this far.
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

    /// <summary>The small badges for due date and reminder.</summary>
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

    /// <param name="tint">Colour for emphasis; null gives a neutral badge.</param>
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
    // Formatting
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
