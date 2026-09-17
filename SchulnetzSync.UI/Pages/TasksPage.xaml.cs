using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchulnetzSync.Core.Tasks;
using SchulnetzSync.UI.Model;
// WinForms ist wegen NotifyIcon aktiviert und kollidiert bei vielen Steuerelementnamen
using Button         = System.Windows.Controls.Button;
using CheckBox       = System.Windows.Controls.CheckBox;
using Cursors        = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox       = System.Windows.Controls.ComboBox;
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
    // Listen-Leiste
    // ══════════════════════════════════════════════════════════════════════

    private void BuildRail(IReadOnlyList<TaskItem> all)
    {
        ListRail.Children.Clear();

        int openAll       = all.Count(t => !t.IsDone);
        int openImportant = all.Count(t => !t.IsDone && t.IsImportant);

        ListRail.Children.Add(RailButton("Alle", ViewAll, openAll, null));
        ListRail.Children.Add(RailButton("★  Wichtig", ViewImportant, openImportant, null));

        ListRail.Children.Add(new Separator
        {
            Style  = (Style)FindResource("Divider"),
            Margin = new Thickness(4, 8, 4, 8)
        });

        foreach (var name in AppState.TaskLists())
        {
            int open = all.Count(t => !t.IsDone
                && string.Equals(t.ListName, name, StringComparison.OrdinalIgnoreCase));
            ListRail.Children.Add(RailButton(name, name, open, ListColor(name)));
        }

        // «Ohne Liste» nur zeigen, wenn es solche Aufgaben gibt
        int orphans = all.Count(t => !t.IsDone && string.IsNullOrWhiteSpace(t.ListName));
        if (orphans > 0 || _selectedList == TaskItem.NoList)
            ListRail.Children.Add(RailButton(TaskItem.NoList, TaskItem.NoList, orphans, null));
    }

    private UIElement RailButton(string label, string key, int count, Color? dot)
    {
        bool selected = _selectedList == key;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (dot is { } c)
        {
            var mark = new Border
            {
                Background        = new SolidColorBrush(c),
                Width             = 8,
                Height            = 8,
                CornerRadius      = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(mark, 0);
            grid.Children.Add(mark);
        }

        var text = new TextBlock
        {
            Text              = label,
            FontSize          = 13,
            FontWeight        = selected ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (count > 0)
        {
            var badge = new TextBlock
            {
                Text              = count.ToString(),
                FontSize          = 12,
                Opacity           = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var item = new Border
        {
            Child        = grid,
            Padding      = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            Margin       = new Thickness(0, 0, 0, 2),
            Cursor       = Cursors.Hand,
            Tag          = key
        };

        if (selected)
            item.SetResourceReference(Border.BackgroundProperty,
                "SystemControlBackgroundChromeMediumLowBrush");

        item.MouseLeftButtonUp += (_, _) =>
        {
            _selectedList = key;
            CloseEditor();
            Refresh();
        };

        return item;
    }

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
                Margin   = new Thickness(2, 0, 0, 4)
            });

            var wrap = new WrapPanel();
            foreach (var name in suggestions)
            {
                var chip = new Button
                {
                    Content  = name,
                    Style    = (Style)FindResource("SecondaryButton"),
                    Padding  = new Thickness(8, 3, 8, 3),
                    FontSize = 11,
                    Margin   = new Thickness(0, 0, 4, 4),
                    Tag      = name
                };
                chip.Click += (_, _) =>
                {
                    AppState.AddTaskList(name);
                    _selectedList         = name;
                    NewListBox.Visibility = Visibility.Collapsed;
                };
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
        if (name.Length == 0) return;

        AppState.AddTaskList(name);
        _selectedList         = name;
        TxtNewList.Text       = "";
        NewListBox.Visibility = Visibility.Collapsed;
    }

    private void BtnDeleteList_Click(object sender, RoutedEventArgs e)
    {
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

        AppState.RemoveTaskList(_selectedList);
        _selectedList = ViewAll;
    }

    private static bool IsPseudoList(string key)
        => key is ViewAll or ViewImportant or TaskItem.NoList;

    // ══════════════════════════════════════════════════════════════════════
    // Formular
    // ══════════════════════════════════════════════════════════════════════

    private void BtnNew_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void OpenEditor(TaskItem? task)
    {
        _editing = task;

        TxtEditorTitle.Text    = task is null ? "NEUE AUFGABE" : "AUFGABE BEARBEITEN";
        TxtEditorError.Text    = "";
        CmbList.ItemsSource    = AppState.TaskLists();
        TxtTitle.Text          = task?.Title ?? "";
        TxtNotes.Text          = task?.Notes ?? "";
        ChkImportant.IsChecked = task?.IsImportant ?? false;

        // Neue Aufgaben landen in der Liste, die gerade offen ist.
        CmbList.Text = task?.ListName
            ?? (IsPseudoList(_selectedList) ? "" : _selectedList);

        SetDateTime(DateDue,    CmbDueTime,    task?.DueAt);
        SetDateTime(DateRemind, CmbRemindTime, task?.RemindAt);

        EditorCard.Visibility = Visibility.Visible;
        TxtTitle.Focus();
    }

    private void CloseEditor()
    {
        _editing = null;
        if (_initialized) EditorCard.Visibility = Visibility.Collapsed;
    }

    private void TxtTitle_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  BtnSave_Click(sender, e);
        if (e.Key == Key.Escape) CloseEditor();
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
        bool reminderIsNew = _editing is null || remind != _editing.RemindAt;
        if (reminderIsNew && remind.HasValue && remind.Value < DateTimeOffset.Now)
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
            // Wurde die Erinnerung verschoben, soll sie erneut ausgelöst werden.
            bool reminderMoved = remind != _editing.RemindAt;

            AppState.UpdateTask(_editing with
            {
                Title         = title,
                ListName      = listName,
                Notes         = notes.Length == 0 ? null : notes,
                DueAt         = due,
                RemindAt      = remind,
                IsImportant   = important,
                ReminderShown = reminderMoved ? false : _editing.ReminderShown,
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

    private void BtnClearDone_Click(object sender, RoutedEventArgs e)
    {
        int done = AppState.Tasks.Count(t => t.IsDone);
        if (done == 0) return;

        var confirm = MessageBox.Show(
            $"{done} erledigte Aufgaben endgültig entfernen?",
            "Erledigte entfernen",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (confirm == MessageBoxResult.Yes) AppState.ClearCompletedTasks();
    }

    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        var all = AppState.Tasks;

        BuildRail(all);

        // Auswahl in der Leiste bestimmt, welche Aufgaben in Frage kommen
        var scoped = (_selectedList switch
        {
            ViewAll         => all,
            ViewImportant   => all.Where(t => t.IsImportant),
            TaskItem.NoList => all.Where(t => string.IsNullOrWhiteSpace(t.ListName)),
            _ => all.Where(t => string.Equals(t.ListName, _selectedList, StringComparison.OrdinalIgnoreCase)),
        }).ToList();

        TxtPageTitle.Text = _selectedList switch
        {
            ViewAll       => "Alle Aufgaben",
            ViewImportant => "Wichtig",
            _             => _selectedList,
        };

        BtnDeleteList.Visibility = IsPseudoList(_selectedList)
            ? Visibility.Collapsed : Visibility.Visible;

        int open    = scoped.Count(t => !t.IsDone);
        int overdue = scoped.Count(t => t.IsOverdue(now));

        TxtSummary.Text = scoped.Count == 0
            ? "Hausaufgaben und To-dos."
            : overdue > 0 ? $"{open} offen · {overdue} überfällig" : $"{open} offen";

        BtnClearDone.IsEnabled = scoped.Any(t => t.IsDone);

        var items = (_filter switch
        {
            "Done" => scoped.Where(t => t.IsDone),
            "All"  => scoped,
            _      => scoped.Where(t => !t.IsDone),
        }).ToList();

        TaskList.Children.Clear();

        if (items.Count == 0)
        {
            ShowEmptyHint();
            return;
        }
        EmptyHint.Visibility = Visibility.Collapsed;

        // In einer einzelnen Liste braucht es keine Gruppenköpfe.
        if (_selectedList is not (ViewAll or ViewImportant))
        {
            foreach (var task in Sort(items))
                TaskList.Children.Add(BuildRow(task, ListColor(task.ListLabel), now));
            return;
        }

        var groups = items
            .GroupBy(t => t.ListLabel)
            .OrderBy(g => g.Key == TaskItem.NoList ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.CurrentCulture);

        foreach (var group in groups)
            TaskList.Children.Add(BuildGroup(group.Key, group, now));
    }

    private void ShowEmptyHint()
    {
        EmptyHint.Visibility = Visibility.Visible;
        (TxtEmptyTitle.Text, TxtEmptyBody.Text) = _filter switch
        {
            "Done" => ("Noch nichts erledigt", "Abgehakte Aufgaben erscheinen hier."),
            "All"  => ("Noch keine Aufgaben", "Lege mit «Neue Aufgabe» deine erste Hausaufgabe an."),
            _      => ("Nichts offen", "Alles erledigt. Neue Aufgaben legst du oben rechts an."),
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
        var color = ListColor(list);
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 8)
        };
        header.Children.Add(new Border
        {
            Background        = new SolidColorBrush(color),
            Width             = 10,
            Height            = 10,
            CornerRadius      = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text              = list,
            FontSize          = 13,
            FontWeight        = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var ordered = Sort(tasks).ToList();

        header.Children.Add(new TextBlock
        {
            Text              = $"  ({ordered.Count})",
            FontSize          = 12,
            Opacity           = 0.55,
            VerticalAlignment = VerticalAlignment.Center
        });
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
            BorderBrush     = new SolidColorBrush(overdue
                                ? Color.FromRgb(0xEF, 0x44, 0x44)
                                : listColor),
            BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(14, 10, 14, 10),
            Margin          = new Thickness(0, 0, 0, 6),
            Opacity         = task.IsDone ? 0.55 : 1.0
        };

        // Theme-Brushes von ModernWpf lassen sich nicht statisch auflösen —
        // FindResource wirft dort. Die Referenz folgt zudem einem Themewechsel.
        card.SetResourceReference(Border.BackgroundProperty,
            "SystemControlBackgroundChromeMediumLowBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckBox
        {
            IsChecked         = task.IsDone,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 10, 0),
            Tag               = task
        };
        check.Checked   += TaskCheck_Changed;
        check.Unchecked += TaskCheck_Changed;
        Grid.SetColumn(check, 0);
        grid.Children.Add(check);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        body.Children.Add(new TextBlock
        {
            Text            = task.Title,
            FontSize        = 14,
            FontWeight      = FontWeights.SemiBold,
            TextWrapping    = TextWrapping.Wrap,
            TextDecorations = task.IsDone ? TextDecorations.Strikethrough : null
        });

        var meta = DescribeSchedule(task, now);
        if (meta.Length > 0)
        {
            var metaText = new TextBlock
            {
                Text     = meta,
                FontSize = 12,
                Margin   = new Thickness(0, 3, 0, 0),
                Opacity  = overdue ? 1.0 : 0.70
            };
            if (overdue)
                metaText.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            else
                metaText.SetResourceReference(TextBlock.ForegroundProperty,
                    "SystemControlForegroundBaseHighBrush");
            body.Children.Add(metaText);
        }

        if (!string.IsNullOrWhiteSpace(task.Notes))
            body.Children.Add(new TextBlock
            {
                Text         = task.Notes,
                FontSize     = 12,
                Opacity      = 0.60,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 4, 0, 0)
            });

        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var actions = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Stern: Priorität direkt in der Zeile umschalten
        var star = new Button
        {
            Content         = task.IsImportant ? "★" : "☆",
            FontSize        = 16,
            Padding         = new Thickness(6, 0, 6, 2),
            Background      = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor          = Cursors.Hand,
            ToolTip         = task.IsImportant
                                ? "Nicht mehr als wichtig markieren"
                                : "Als wichtig markieren",
            Foreground      = task.IsImportant
                                ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))
                                : System.Windows.Media.Brushes.Gray,
            Tag             = task
        };
        star.Click += TaskStar_Click;
        actions.Children.Add(star);

        var edit = new Button
        {
            Content  = "Bearbeiten",
            Style    = (Style)FindResource("SecondaryButton"),
            Padding  = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            Margin   = new Thickness(6, 0, 6, 0),
            Tag      = task
        };
        edit.Click += (_, _) => OpenEditor(task);
        actions.Children.Add(edit);

        var delete = new Button
        {
            Content  = "🗑",
            Style    = (Style)FindResource("SecondaryButton"),
            Padding  = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            ToolTip  = "Aufgabe löschen",
            Tag      = task
        };
        delete.Click += TaskDelete_Click;
        actions.Children.Add(delete);

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private void TaskStar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TaskItem task }) return;
        AppState.UpdateTask(task with { IsImportant = !task.IsImportant });
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

    private void TaskDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TaskItem task }) return;

        var confirm = MessageBox.Show(
            $"«{task.Title}» löschen?",
            "Aufgabe löschen",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (confirm == MessageBoxResult.Yes) AppState.RemoveTask(task.Id);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Darstellung
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Beschreibt Abgabe und Erinnerung in einer Zeile.</summary>
    private static string DescribeSchedule(TaskItem task, DateTimeOffset now)
    {
        var parts = new List<string>();

        if (task.DueAt is { } due)
        {
            var when = FormatDay(due);
            var time = due.TimeOfDay == new TimeSpan(23, 59, 0) ? "" : $", {due:HH\\:mm}";

            parts.Add(task.IsDone
                ? $"Abgabe war {when}{time}"
                : due < now
                    ? $"Überfällig seit {when}{time}"
                    : $"Abgabe {when}{time}");
        }

        if (!task.IsDone && task.RemindAt is { } remind && remind > now)
            parts.Add($"Erinnerung {FormatDay(remind).ToLowerInvariant()}");

        return string.Join("  ·  ", parts);
    }

    private static string FormatDay(DateTimeOffset value)
    {
        int days = (value.Date - DateTime.Today).Days;
        return days switch
        {
            0  => "heute",
            1  => "morgen",
            -1 => "gestern",
            > 1 and < 7 => value.ToString("dddd", DeCh),
            _  => value.ToString("ddd, d. MMM", DeCh),
        };
    }

    /// <summary>Nutzt dieselbe Farbe wie das Fach im Kalender, sofern es eine gibt.</summary>
    private static Color ListColor(string list)
    {
        var key = list == TaskItem.NoList ? "Lektion" : list;
        try { return (Color)ColorConverter.ConvertFromString(AppState.GetEventColor(key)); }
        catch { return Color.FromRgb(0x25, 0x63, 0xEB); }
    }
}
