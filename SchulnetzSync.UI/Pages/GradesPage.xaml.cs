using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Grades;

// Pin down the WPF versions of names WinForms also uses
using WpfBorder      = System.Windows.Controls.Border;
using WpfButton      = System.Windows.Controls.Button;
using WpfComboBox    = System.Windows.Controls.ComboBox;
using WpfControl     = System.Windows.Controls.Control;
using WpfHA          = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPage        = System.Windows.Controls.Page;
using WpfPath        = System.Windows.Shapes.Path;
using WpfTextBlock   = System.Windows.Controls.TextBlock;
using WpfTextBox     = System.Windows.Controls.TextBox;

namespace SchulnetzSync.UI.Pages;

/// <summary>
/// Grades typed in by hand.
///
///   header      title, summary, help, rule, «Note eintragen»
///   overview    four tiles: promotion, overall average, plus points, insufficient subjects
///   conditions  the rule's conditions as chips, each ticked or crossed
///   subjects    a table, one row per subject with all its grades as chips
///   detail      the selected subject: its grades and the target calculator
///
/// Entering a grade and choosing the rule happen in a dialog on top of the page, and the
/// first visit gets a short guided tour. Everything is built in code from the computed
/// standings, like the calendar, and rebuilt after each change.
/// </summary>
public partial class GradesPage : WpfPage
{
    private static readonly CultureInfo _deCH = CultureInfo.GetCultureInfo("de-CH");

    private static readonly Color _success = Color.FromRgb(0x16, 0xA3, 0x4A);
    private static readonly Color _danger  = Color.FromRgb(0xDC, 0x26, 0x26);
    private static readonly Color _accent  = Color.FromRgb(0x5C, 0x6E, 0xF7);
    private static readonly Color _neutral = Color.FromRgb(0x80, 0x80, 0x80);

    /// <summary>Below this page width the detail moves under the table.</summary>
    private const double NarrowWidth = 1000;

    private string? _selectedSubject;

    // Target calculator input; survives a rebuild of the page
    private double _targetGrade  = 4.0;
    private string _targetWeight = "1";

    /// <summary>The calculator card, kept for the tutorial to point at.</summary>
    private FrameworkElement? _calculatorCard;

    public GradesPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AppState.Changed += OnStateChanged;
            Refresh();
            if (!AppState.Config.GradesTutorialSeen)
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => StartTutorial());
        };
        Unloaded    += (_, _) => AppState.Changed -= OnStateChanged;
        SizeChanged += (_, _) => { ApplyLayoutWidth(); PositionTutorial(); };
        Scroller.ScrollChanged += (_, _) => PositionTutorial();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnStateChanged() => Dispatcher.Invoke(Refresh);

    // ══════════════════════════════════════════════════════════════════════
    // Rebuilding
    // ══════════════════════════════════════════════════════════════════════

    private void Refresh()
    {
        var standings = Grading.Summarize(AppState.Grades);
        var rule      = AppState.PromotionRule;
        var result    = rule.Evaluate(standings);

        if (_selectedSubject is null || standings.All(s => s.Subject != _selectedSubject))
            _selectedSubject = standings.FirstOrDefault()?.Subject;

        TxtSummary.Text = standings.Count == 0
            ? "Trag deine Noten ein. Semestria rechnet Schnitte, Pluspunkte und sagt dir, ob die Promotion reicht."
            : $"{Count(AppState.Grades.Count, "Note", "Noten")} in {Count(standings.Count, "Fach", "Fächern")}  ·  " +
              "Fachnoten auf halbe Noten gerundet";
        BtnRule.Content = $"Regel: {rule.Name}";

        BuildKpis(standings, result, rule);
        BuildConditionChips(result);
        BuildSubjectTable(standings);
        BuildDetail(standings);
        ApplyLayoutWidth();
    }

    /// <summary>Wide: table and detail side by side. Narrow: detail below the table.</summary>
    private void ApplyLayoutWidth()
    {
        bool narrow = ActualWidth > 0 && ActualWidth < NarrowWidth;
        GapColumn.Width    = narrow ? new GridLength(0) : new GridLength(20);
        DetailColumn.Width = narrow ? new GridLength(0) : new GridLength(360);
        Grid.SetColumn(DetailPane, narrow ? 0 : 2);
        Grid.SetRow(DetailPane, narrow ? 1 : 0);
        DetailPane.Margin = narrow ? new Thickness(0, 4, 0, 0) : new Thickness(0);
        KpiStrip.Columns  = ActualWidth > 0 && ActualWidth < 760 ? 2 : 4;
    }

    // ── Overview tiles ───────────────────────────────────────────────────────

    private void BuildKpis(IReadOnlyList<SubjectStanding> standings, PromotionResult result, PromotionRule rule)
    {
        KpiStrip.Children.Clear();

        var (statusText, statusColor) = result.Status switch
        {
            ConditionStatus.Met    => ("Erreicht", _success),
            ConditionStatus.Failed => ("Gefährdet", _danger),
            _                      => ("Noch offen", _neutral)
        };
        KpiStrip.Children.Add(Kpi("PROMOTION", statusText, $"nach Regel «{rule.Name}»", statusColor));

        var avg = Grading.OverallAverage(standings);
        KpiStrip.Children.Add(Kpi("GESAMTSCHNITT",
            avg is double a ? a.ToString("0.00", _deCH) : "–",
            standings.Count == 0 ? "noch keine Noten" : $"aus {Count(standings.Count, "Fachnote", "Fachnoten")}",
            avg is double v && v < GradeEntry.Sufficient ? _danger : null));

        double points = Grading.PlusPoints(standings);
        KpiStrip.Children.Add(Kpi("PLUSPUNKTE",
            standings.Count == 0 ? "–" : SignedPoints(points),
            "+0.5 je halbe Note über 4, −1 darunter",
            standings.Count > 0 && points < 0 ? _danger : null));

        var insufficient = standings.Where(s => s.IsInsufficient).Select(s => s.Subject).ToList();
        KpiStrip.Children.Add(Kpi("UNGENÜGEND",
            Count(insufficient.Count, "Fach", "Fächer"),
            insufficient.Count == 0 ? "alles mindestens 4" : string.Join(", ", insufficient),
            insufficient.Count > 0 ? _danger : null));
    }

    private UIElement Kpi(string label, string value, string hint, Color? color)
    {
        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.Bold, Opacity = 0.55
        });
        var valueText = new WpfTextBlock
        {
            Text = value, FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 2)
        };
        if (color is Color c) valueText.Foreground = new SolidColorBrush(c);
        panel.Children.Add(valueText);
        panel.Children.Add(new WpfTextBlock
        {
            Text = hint, FontSize = 12, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = hint
        });

        var card = new WpfBorder
        {
            Style = (Style)FindResource("Card"), Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(6, 0, 6, 12), Child = panel
        };
        if (color is Color edge)
        {
            card.BorderBrush     = new SolidColorBrush(edge);
            card.BorderThickness = new Thickness(3, 1, 1, 1);
        }
        return card;
    }

    // ── Rule conditions as chips ─────────────────────────────────────────────

    private void BuildConditionChips(PromotionResult result)
    {
        ConditionChips.Children.Clear();
        foreach (var c in result.Conditions)
        {
            var (glyph, color) = c.Status switch
            {
                ConditionStatus.Met    => ("✓", _success),
                ConditionStatus.Failed => ("✕", _danger),
                _                      => ("○", _neutral)
            };
            var text = new WpfTextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            text.Inlines.Add(new System.Windows.Documents.Run(glyph + "  ")
            {
                Foreground = new SolidColorBrush(color), FontWeight = FontWeights.Bold
            });
            text.Inlines.Add(new System.Windows.Documents.Run(c.Label));
            text.Inlines.Add(new System.Windows.Documents.Run("  ·  " + c.Detail) { Foreground = Brush(0x99) });

            ConditionChips.Children.Add(new WpfBorder
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 6),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(Color.FromArgb(24, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                Child = text
            });
        }
    }

    // ── Subject table ────────────────────────────────────────────────────────

    // Column widths shared by header and rows, so everything lines up
    private static readonly GridLength[] _cols =
    [
        new(72), new(1, GridUnitType.Star), new(64), new(76), new(64)
    ];

    private void BuildSubjectTable(IReadOnlyList<SubjectStanding> standings)
    {
        SubjectTable.Children.Clear();

        if (standings.Count == 0)
        {
            SubjectTable.Children.Add(EmptyState());
            return;
        }

        var header = TableGrid(new Thickness(18, 14, 18, 10));
        AddCell(header, 0, HeaderText("FACH"));
        AddCell(header, 1, HeaderText("NOTEN"));
        AddCell(header, 2, HeaderText("SCHNITT", right: true));
        AddCell(header, 3, HeaderText("FACHNOTE", right: true));
        AddCell(header, 4, HeaderText("PUNKTE", right: true));
        SubjectTable.Children.Add(header);
        SubjectTable.Children.Add(Divider());

        foreach (var s in standings)
            SubjectTable.Children.Add(SubjectRow(s));
    }

    private UIElement SubjectRow(SubjectStanding s)
    {
        bool isSel = s.Subject == _selectedSubject;
        var grid = TableGrid(new Thickness(0));

        AddCell(grid, 0, new WpfTextBlock
        {
            Text = s.Subject, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });

        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var g in GradesOf(s.Subject))
            chips.Children.Add(GradeChip(g));
        AddCell(grid, 1, chips);

        AddCell(grid, 2, new WpfTextBlock
        {
            Text = s.Average.ToString("0.00", _deCH), FontSize = 13, Opacity = 0.75,
            HorizontalAlignment = WpfHA.Right, VerticalAlignment = VerticalAlignment.Center
        });
        AddCell(grid, 3, GradeBadge(s.Grade, large: false));
        AddCell(grid, 4, new WpfTextBlock
        {
            Text = SignedPoints(s.PlusPoints), FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = WpfHA.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = s.PlusPoints < 0 ? new SolidColorBrush(_danger)
                       : s.PlusPoints > 0 ? new SolidColorBrush(_success)
                       : Brush(0xB0)
        });

        var row = new WpfButton
        {
            Content = grid,
            Style = RowButtonStyle(),
            Padding = new Thickness(18, 10, 18, 10),
            Background = isSel ? new SolidColorBrush(Color.FromArgb(30, _accent.R, _accent.G, _accent.B))
                               : System.Windows.Media.Brushes.Transparent,
            ToolTip = $"{s.Subject}: Details und Zielrechner"
        };
        System.Windows.Automation.AutomationProperties.SetName(row, $"Fach {s.Subject}");
        row.Click += (_, _) => { _selectedSubject = s.Subject; Refresh(); };
        return row;
    }

    private UIElement EmptyState()
    {
        var panel = new StackPanel { Margin = new Thickness(32, 40, 32, 44), HorizontalAlignment = WpfHA.Center };
        panel.Children.Add(new WpfTextBlock
        {
            Text = "", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 34, Opacity = 0.5, HorizontalAlignment = WpfHA.Center
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = "Noch keine Noten", FontSize = 18, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = WpfHA.Center, Margin = new Thickness(0, 12, 0, 4)
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = "Trag deine erste Note ein. Die Fächer aus deinem Stundenplan werden vorgeschlagen.",
            FontSize = 13, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxWidth = 360, HorizontalAlignment = WpfHA.Center
        });
        var btn = new WpfButton
        {
            Content = "+  Erste Note eintragen", Style = (Style)FindResource("PrimaryButton"),
            HorizontalAlignment = WpfHA.Center, Margin = new Thickness(0, 18, 0, 0)
        };
        btn.Click += (_, _) => ShowGradeDialog(null, null);
        panel.Children.Add(btn);
        return panel;
    }

    // ── Detail of the selected subject ───────────────────────────────────────

    private void BuildDetail(IReadOnlyList<SubjectStanding> standings)
    {
        DetailPane.Children.Clear();
        var s = standings.FirstOrDefault(x => x.Subject == _selectedSubject);

        if (s is not null)
            DetailPane.Children.Add(SubjectDetailCard(s));

        _calculatorCard = BuildTargetCalculator(s);
        DetailPane.Children.Add(_calculatorCard);
    }

    private UIElement SubjectDetailCard(SubjectStanding s)
    {
        var panel = new StackPanel();

        var head = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new WpfTextBlock { Text = s.Subject, FontSize = 20, FontWeight = FontWeights.Bold });
        title.Children.Add(new WpfTextBlock
        {
            Text = $"Schnitt {s.Average.ToString("0.000", _deCH)}  ·  Pluspunkte {SignedPoints(s.PlusPoints)}",
            FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 2, 0, 0)
        });
        head.Children.Add(title);
        var badge = GradeBadge(s.Grade, large: true);
        Grid.SetColumn(badge, 1);
        head.Children.Add(badge);
        panel.Children.Add(head);

        panel.Children.Add(new WpfTextBlock
        {
            Text = "NOTEN", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 14, 0, 6)
        });
        foreach (var g in GradesOf(s.Subject).Reverse())
            panel.Children.Add(GradeRow(g));

        var add = new WpfButton
        {
            Content = $"+  Note in {s.Subject}", Style = (Style)FindResource("SecondaryButton"),
            HorizontalAlignment = WpfHA.Left, Margin = new Thickness(0, 12, 0, 0)
        };
        add.Click += (_, _) => ShowGradeDialog(null, s.Subject);
        panel.Children.Add(add);

        return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
    }

    private UIElement GradeRow(GradeEntry g)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new WpfTextBlock
        {
            Text = FormatGrade(g.Value), FontSize = 16, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = g.Value < GradeEntry.Sufficient ? new SolidColorBrush(_danger) : Brush(0xFF)
        });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new WpfTextBlock
        {
            Text = string.IsNullOrWhiteSpace(g.Title) ? "Note" : g.Title, FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        info.Children.Add(new WpfTextBlock
        {
            Text = g.Date.ToString("d. MMMM yyyy", _deCH) + (g.Weight == 1 ? "" : $"  ·  Gewicht {g.Weight.ToString("0.##", _deCH)}"),
            FontSize = 11, Opacity = 0.6
        });
        Grid.SetColumn(info, 1);
        row.Children.Add(info);

        var actions = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var edit = IconButton("", $"Note {FormatGrade(g.Value)} bearbeiten");
        edit.Click += (_, _) => ShowGradeDialog(g, null);
        var delete = IconButton("", $"Note {FormatGrade(g.Value)} löschen");
        delete.Click += (_, _) =>
        {
            var name = string.IsNullOrWhiteSpace(g.Title) ? $"die {FormatGrade(g.Value)} in {g.Subject}" : $"«{g.Title}»";
            if (MessageBox.Show($"{name} wirklich löschen?", "Note löschen",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                AppState.RemoveGrade(g.Id);
        };
        actions.Children.Add(edit);
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return row;
    }

    // ── Target calculator ────────────────────────────────────────────────────

    private FrameworkElement BuildTargetCalculator(SubjectStanding? standing)
    {
        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = "ZIELRECHNER", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 0, 0, 6)
        });

        if (_selectedSubject is null)
        {
            panel.Children.Add(new WpfTextBlock
            {
                Text = "Sobald ein Fach Noten hat, rechnet Semestria hier aus, was die nächste Prüfung braucht.",
                Style = (Style)FindResource("NoteText")
            });
            return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
        }

        panel.Children.Add(new WpfTextBlock
        {
            Text = $"Was brauchst du in der nächsten {_selectedSubject}-Prüfung?",
            FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        var cmbTarget = new WpfComboBox { Height = 34, FontSize = 13, Width = 90 };
        for (double t = 3.5; t <= 6.0; t += 0.5) cmbTarget.Items.Add(FormatGrade(t));
        cmbTarget.SelectedItem = FormatGrade(_targetGrade);
        System.Windows.Automation.AutomationProperties.SetName(cmbTarget, "Ziel-Fachnote");

        var txtWeight = new WpfTextBox { Height = 34, FontSize = 13, Width = 70, Text = _targetWeight };
        System.Windows.Automation.AutomationProperties.SetName(txtWeight, "Gewicht der Prüfung");

        var fields = new WrapPanel();
        fields.Children.Add(Labeled("Ziel-Fachnote", cmbTarget));
        fields.Children.Add(Labeled("Gewicht der Prüfung", txtWeight));
        panel.Children.Add(fields);

        var resultBox = new WpfBorder
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 12)
        };
        var resultText = new WpfTextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        resultBox.Child = resultText;
        panel.Children.Add(resultBox);

        void Update()
        {
            if (cmbTarget.SelectedItem is string t && TryParseNumber(t, out var target)) _targetGrade = target;
            _targetWeight = txtWeight.Text;

            Color tone;
            if (!TryParseNumber(_targetWeight, out var w) || w <= 0)
            {
                resultText.Text = "Das Gewicht muss grösser als 0 sein.";
                tone = _danger;
            }
            else
            {
                double needed = Grading.NeededGrade(standing, w, _targetGrade);
                // Grades are written to a tenth, so round the requirement up to the next one
                double shown = Math.Ceiling(Math.Round(needed * 10, 6)) / 10;
                (resultText.Text, tone) = needed switch
                {
                    <= GradeEntry.Min => ($"Sicher: Selbst mit einer 1 bleibt {_selectedSubject} mindestens bei {FormatGrade(_targetGrade)}.", _success),
                    > GradeEntry.Max  => ($"Mit einer Prüfung nicht erreichbar: Du bräuchtest eine {shown.ToString("0.0", _deCH)}.", _danger),
                    _                 => ($"Du brauchst mindestens eine {shown.ToString("0.0", _deCH)}.", _accent)
                };
            }
            resultBox.Background = new SolidColorBrush(Color.FromArgb(28, tone.R, tone.G, tone.B));
            resultText.Foreground = new SolidColorBrush(tone);
        }

        cmbTarget.SelectionChanged += (_, _) => Update();
        txtWeight.TextChanged      += (_, _) => Update();
        Update();

        return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
    }

    // ══════════════════════════════════════════════════════════════════════
    // Dialogs
    // ══════════════════════════════════════════════════════════════════════

    private void BtnAddGrade_Click(object sender, RoutedEventArgs e) => ShowGradeDialog(null, _selectedSubject);

    private void BtnRule_Click(object sender, RoutedEventArgs e)
    {
        LoadDraft(AppState.PromotionRule);
        ShowRuleDialog();
    }

    private void OpenDialog(string title, string? subtitle)
    {
        DialogContent.Children.Clear();
        DialogContent.Children.Add(new WpfTextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold });
        if (subtitle is not null)
            DialogContent.Children.Add(new WpfTextBlock
            {
                Text = subtitle, FontSize = 13, Opacity = 0.65, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        DialogContent.Children.Add(new WpfBorder { Height = 16 });
        DialogLayer.Visibility = Visibility.Visible;
    }

    private void CloseDialog()
    {
        DialogLayer.Visibility = Visibility.Collapsed;
        DialogContent.Children.Clear();
    }

    // ── Entering or editing a grade ──────────────────────────────────────────

    private void ShowGradeDialog(GradeEntry? editing, string? presetSubject)
    {
        OpenDialog(editing is null ? "Note eintragen" : "Note bearbeiten",
                   editing is null ? "Eine Note pro Prüfung. Halbe Prüfungen bekommen Gewicht ½." : null);

        var cmbSubject = new WpfComboBox { IsEditable = true, Height = 36, FontSize = 14, Width = 140 };
        foreach (var code in AppState.GradeSubjectSuggestions()) cmbSubject.Items.Add(code);
        cmbSubject.Text = editing?.Subject ?? presetSubject ?? "";
        System.Windows.Automation.AutomationProperties.SetName(cmbSubject, "Fach");

        var txtValue = new WpfTextBox
        {
            Height = 36, FontSize = 14, Width = 90, Text = editing is null ? "" : FormatGrade(editing.Value)
        };
        System.Windows.Automation.AutomationProperties.SetName(txtValue, "Note");

        var top = new WrapPanel();
        top.Children.Add(Labeled("Fach", cmbSubject));
        top.Children.Add(Labeled("Note (1 bis 6)", txtValue));
        DialogContent.Children.Add(top);

        // Weight: three quick picks plus a box for anything else
        var txtWeight = new WpfTextBox
        {
            Height = 36, FontSize = 14, Width = 70, Text = (editing?.Weight ?? 1).ToString("0.##", _deCH)
        };
        System.Windows.Automation.AutomationProperties.SetName(txtWeight, "Gewicht");
        var weightRow = new StackPanel { Orientation = WpfOrientation.Horizontal };
        foreach (var (label, value) in new[] { ("½", "0.5"), ("1", "1"), ("2", "2") })
        {
            var chip = new WpfButton
            {
                Content = label, MinWidth = 44, Height = 36, Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(12, 0, 12, 0), FontSize = 14, FontWeight = FontWeights.SemiBold,
                Style = (Style)FindResource("SecondaryButton"), ToolTip = $"Gewicht {value}"
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, $"Gewicht {value}");
            chip.Click += (_, _) => txtWeight.Text = value;
            weightRow.Children.Add(chip);
        }
        weightRow.Children.Add(txtWeight);
        DialogContent.Children.Add(Labeled("Gewicht", weightRow));

        var dpDate = new DatePicker
        {
            Height = 36, FontSize = 13, Width = 160,
            SelectedDate = (editing?.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue)
        };
        var txtTitle = new WpfTextBox { Height = 36, FontSize = 13, Text = editing?.Title ?? "" };
        System.Windows.Automation.AutomationProperties.SetName(txtTitle, "Wofür");
        var dateAndTitle = new Grid();
        dateAndTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dateAndTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dateAndTitle.Children.Add(Labeled("Datum", dpDate));
        var titleField = Labeled("Wofür (optional)", txtTitle, stretch: true);
        Grid.SetColumn(titleField, 1);
        dateAndTitle.Children.Add(titleField);
        DialogContent.Children.Add(dateAndTitle);

        var error = ErrorText();
        DialogContent.Children.Add(error);

        var save = new WpfButton
        {
            Content = editing is null ? "Note speichern" : "Änderungen speichern",
            Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0)
        };
        void Save()
        {
            var problem = TrySaveGrade(editing, cmbSubject.Text, txtValue.Text, txtWeight.Text, txtTitle.Text, dpDate.SelectedDate);
            if (problem is null) { CloseDialog(); return; }
            error.Text = problem;
            error.Visibility = Visibility.Visible;
        }
        save.Click += (_, _) => Save();
        DialogContent.Children.Add(DialogButtons(save));

        foreach (var box in new UIElement[] { cmbSubject, txtValue, txtWeight, txtTitle })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };

        // Straight into the field that is still missing
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (cmbSubject.Text.Length == 0) cmbSubject.Focus(); else txtValue.Focus();
        });
    }

    /// <summary>Validates and stores a grade. Returns an error message, or null on success.</summary>
    private string? TrySaveGrade(GradeEntry? editing, string subject, string value, string weight, string title, DateTime? date)
    {
        subject = subject.Trim().ToUpperInvariant();
        if (subject.Length == 0)
            return "Wähle oder tippe ein Fach, zum Beispiel INF.";
        if (!TryParseNumber(value, out var v) || v < GradeEntry.Min || v > GradeEntry.Max)
            return "Die Note muss zwischen 1 und 6 liegen, zum Beispiel 4.5.";
        if (!TryParseNumber(weight, out var w) || w <= 0)
            return "Das Gewicht muss grösser als 0 sein, zum Beispiel 1 oder 0.5.";

        _selectedSubject = subject;
        AppState.SaveGrade(new GradeEntry(
            Id:      editing?.Id ?? Guid.NewGuid(),
            Subject: subject,
            Value:   v,
            Weight:  w,
            Title:   title.Trim(),
            Date:    DateOnly.FromDateTime(date ?? DateTime.Today)));   // fires Notify, which rebuilds the page
        return null;
    }

    // ── Rule ─────────────────────────────────────────────────────────────────

    /// <summary>One condition while it is being edited; values stay text until saved.</summary>
    private sealed class DraftCondition
    {
        public required string Kind;
        public string Value   = "";
        public string Subject = "";
    }

    private string?               _draftName;
    private List<DraftCondition>? _draft;

    private void LoadDraft(PromotionRule rule)
    {
        _draftName = rule.Name;
        _draft = rule.Conditions.Select(c => c switch
        {
            MinPlusPoints p           => new DraftCondition { Kind = "plus",    Value = Num(p.Min) },
            MaxInsufficientSubjects m => new DraftCondition { Kind = "maxInsu", Value = m.Max.ToString(CultureInfo.InvariantCulture) },
            MinOverallAverage a       => new DraftCondition { Kind = "average", Value = Num(a.Min) },
            MinSubjectGrade s         => new DraftCondition { Kind = "subject", Value = Num(s.Min), Subject = s.Subject },
            _                         => throw new InvalidOperationException($"Unknown condition {c.GetType().Name}")
        }).ToList();

        static string Num(double d) => d.ToString("0.##", _deCH);
    }

    private void ShowRuleDialog()
    {
        OpenDialog("Promotionsregel", "Wähle das System deiner Schule und pass es an. Alle Bedingungen müssen erfüllt sein.");

        var templates = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 16) };
        templates.Children.Add(TemplateCard("Pluspunkte", "Pluspunkte ≥ 0, höchstens 2 ungenügend", PromotionRule.PlusPointsSystem));
        templates.Children.Add(TemplateCard("Gesamtschnitt", "Schnitt ≥ 4, höchstens 2 ungenügend", PromotionRule.AverageSystem));
        templates.Children.Add(TemplateCard("Eigene", "Leer beginnen und selbst bauen", new PromotionRule("Eigene Regel", [])));
        DialogContent.Children.Add(Labeled("Vorlage", templates, stretch: true));

        var txtName = new WpfTextBox { Height = 34, FontSize = 13, Text = _draftName ?? "" };
        txtName.TextChanged += (_, _) => _draftName = txtName.Text;
        DialogContent.Children.Add(Labeled("Name", txtName, stretch: true));

        DialogContent.Children.Add(new WpfTextBlock
        {
            Text = "BEDINGUNGEN", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 4, 0, 6)
        });
        if (_draft!.Count == 0)
            DialogContent.Children.Add(new WpfTextBlock { Text = "Noch keine. Füge unten welche hinzu.", Style = (Style)FindResource("NoteText") });
        foreach (var d in _draft)
            DialogContent.Children.Add(DraftRow(d));

        var adders = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        adders.Children.Add(AddConditionButton("+ Pluspunkte",          new DraftCondition { Kind = "plus",    Value = "0" }));
        adders.Children.Add(AddConditionButton("+ Ungenügende Fächer",  new DraftCondition { Kind = "maxInsu", Value = "2" }));
        adders.Children.Add(AddConditionButton("+ Gesamtschnitt",       new DraftCondition { Kind = "average", Value = "4" }));
        adders.Children.Add(AddConditionButton("+ Mindestnote in Fach", new DraftCondition { Kind = "subject", Value = "4", Subject = _selectedSubject ?? "" }));
        DialogContent.Children.Add(adders);

        DialogContent.Children.Add(new WpfTextBlock
        {
            Text = "Schlüsselfächer wie Informatik an der IMS: «+ Mindestnote in Fach» mit INF und 4.",
            Style = (Style)FindResource("NoteText"), Margin = new Thickness(0, 0, 0, 12)
        });

        var error = ErrorText();
        DialogContent.Children.Add(error);

        var save = new WpfButton { Content = "Regel speichern", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) =>
        {
            if (TryBuildRule(out var rule, out var problem))
            {
                CloseDialog();
                _draft = null;
                AppState.PromotionRule = rule;   // fires Notify, which rebuilds the page
                return;
            }
            error.Text       = problem;
            error.Visibility = Visibility.Visible;
        };
        DialogContent.Children.Add(DialogButtons(save));
    }

    private UIElement TemplateCard(string title, string text, PromotionRule template)
    {
        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new WpfTextBlock { Text = text, FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var btn = new WpfButton
        {
            Content = panel, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0),
            HorizontalContentAlignment = WpfHA.Left, VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(10, 8, 10, 8)
        };
        System.Windows.Automation.AutomationProperties.SetName(btn, $"Vorlage {title}");
        btn.Click += (_, _) => { LoadDraft(template); ShowRuleDialog(); };
        return btn;
    }

    private WpfButton AddConditionButton(string label, DraftCondition condition)
    {
        var btn = new WpfButton { Content = label, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 8) };
        btn.Click += (_, _) => { _draft!.Add(condition); ShowRuleDialog(); };
        return btn;
    }

    private UIElement DraftRow(DraftCondition d)
    {
        var row = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };

        WpfTextBlock Text(string t) => new()
        {
            Text = t, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        };
        WpfTextBox Box(string value, double width, Action<string> set)
        {
            var box = new WpfTextBox { Text = value, Width = width, Height = 32, FontSize = 13, Margin = new Thickness(0, 0, 8, 0) };
            box.TextChanged += (_, _) => set(box.Text);
            return box;
        }

        switch (d.Kind)
        {
            case "plus":
                row.Children.Add(Text("Pluspunkte mindestens"));
                row.Children.Add(Box(d.Value, 60, v => d.Value = v));
                break;
            case "maxInsu":
                row.Children.Add(Text("Höchstens"));
                row.Children.Add(Box(d.Value, 50, v => d.Value = v));
                row.Children.Add(Text("Fächer ungenügend"));
                break;
            case "average":
                row.Children.Add(Text("Gesamtschnitt mindestens"));
                row.Children.Add(Box(d.Value, 60, v => d.Value = v));
                break;
            case "subject":
                row.Children.Add(Text("Fach"));
                row.Children.Add(Box(d.Subject, 70, v => d.Subject = v));
                row.Children.Add(Text("mindestens"));
                row.Children.Add(Box(d.Value, 60, v => d.Value = v));
                break;
        }

        var remove = IconButton("", "Bedingung entfernen");
        remove.Click += (_, _) => { _draft!.Remove(d); ShowRuleDialog(); };
        row.Children.Add(remove);
        return row;
    }

    private bool TryBuildRule(out PromotionRule rule, out string problem)
    {
        rule    = AppState.PromotionRule;
        problem = "";
        var conditions = new List<RuleCondition>();

        foreach (var d in _draft!)
        {
            if (d.Kind == "maxInsu")
            {
                if (!int.TryParse(d.Value.Trim(), out var max) || max < 0)
                { problem = "Die Anzahl ungenügender Fächer muss eine ganze Zahl ab 0 sein."; return false; }
                conditions.Add(new MaxInsufficientSubjects(max));
                continue;
            }

            if (!TryParseNumber(d.Value, out var value))
            { problem = $"«{d.Value}» ist keine Zahl."; return false; }

            switch (d.Kind)
            {
                case "plus":
                    conditions.Add(new MinPlusPoints(value));
                    break;
                case "average" when value is < GradeEntry.Min or > GradeEntry.Max:
                case "subject" when value is < GradeEntry.Min or > GradeEntry.Max:
                    problem = "Noten in einer Regel müssen zwischen 1 und 6 liegen.";
                    return false;
                case "average":
                    conditions.Add(new MinOverallAverage(value));
                    break;
                case "subject":
                    var subject = d.Subject.Trim().ToUpperInvariant();
                    if (subject.Length == 0) { problem = "Bei «Mindestnote in Fach» fehlt das Fach."; return false; }
                    conditions.Add(new MinSubjectGrade(subject, value));
                    break;
            }
        }

        var name = string.IsNullOrWhiteSpace(_draftName) ? "Eigene Regel" : _draftName.Trim();
        rule = new PromotionRule(name, conditions);
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Tutorial
    // ══════════════════════════════════════════════════════════════════════

    private sealed record TourStep(Func<FrameworkElement?> Target, string Title, string Text);

    private IReadOnlyList<TourStep> TourSteps() =>
    [
        new(() => BtnAddGrade, "Note eintragen",
            "Hier trägst du eine Note ein: Fach, Note und Gewicht. Eine halbe Prüfung bekommt Gewicht ½."),
        new(() => KpiStrip, "Dein Überblick",
            "Promotion, Gesamtschnitt, Pluspunkte und ungenügende Fächer auf einen Blick. Rot heisst: hier aufpassen."),
        new(() => BtnRule, "Die Regel deiner Schule",
            "Pluspunkte oder Gesamtschnitt? Hier wählst du das System. Schlüsselfächer wie Informatik an der IMS legst du ebenfalls hier fest."),
        new(() => SubjectCard, "Deine Fächer",
            "Jede Zeile ist ein Fach: alle Noten, der genaue Schnitt und die auf halbe Noten gerundete Fachnote. Klick auf ein Fach für Details."),
        new(() => _calculatorCard, "Zielrechner",
            "Wähl eine Ziel-Fachnote und das Gewicht der nächsten Prüfung. Semestria sagt dir, welche Note du dafür brauchst."),
        new(() => BtnHelp, "Jederzeit wieder",
            "Mit dem Fragezeichen startest du diese Anleitung noch einmal."),
    ];

    private int _tourIndex = -1;

    private void BtnHelp_Click(object sender, RoutedEventArgs e) => StartTutorial();

    private void StartTutorial()
    {
        CloseDialog();
        _tourIndex = 0;
        TutorialLayer.Visibility = Visibility.Visible;
        ShowTourStep();
    }

    private void EndTutorial()
    {
        _tourIndex = -1;
        TutorialLayer.Visibility = Visibility.Collapsed;
        TutorialLayer.Children.Clear();

        if (!AppState.Config.GradesTutorialSeen)
        {
            AppState.Config.GradesTutorialSeen = true;
            ConfigManager.Save(AppState.Config);
        }
    }

    private void ShowTourStep()
    {
        var steps = TourSteps();
        if (_tourIndex < 0 || _tourIndex >= steps.Count) { EndTutorial(); return; }

        // Scroll the element into view first, then measure once the layout has settled
        steps[_tourIndex].Target()?.BringIntoView();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, PositionTutorial);
    }

    /// <summary>Dims the page, cuts a hole around the current element and places the bubble.</summary>
    private void PositionTutorial()
    {
        if (_tourIndex < 0 || TutorialLayer.Visibility != Visibility.Visible) return;

        var steps  = TourSteps();
        var step   = steps[_tourIndex];
        var target = step.Target();
        double w = TutorialLayer.ActualWidth, h = TutorialLayer.ActualHeight;
        if (w == 0 || h == 0) return;

        TutorialLayer.Children.Clear();

        Rect hole = Rect.Empty;
        if (target is { IsVisible: true } && target.IsDescendantOf(Root))
        {
            // TransformToVisual, not ToAncestor: the layer is a sibling of the content
            hole = target.TransformToVisual(TutorialLayer)
                         .TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            hole.Inflate(8, 8);
        }

        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry(new Rect(0, 0, w, h)));
        if (!hole.IsEmpty) dim.Children.Add(new RectangleGeometry(hole, 12, 12));
        TutorialLayer.Children.Add(new WpfPath { Data = dim, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)) });

        if (!hole.IsEmpty)
        {
            var ring = new WpfBorder
            {
                Width = hole.Width, Height = hole.Height, CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(_accent), BorderThickness = new Thickness(2),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, hole.X);
            Canvas.SetTop(ring, hole.Y);
            TutorialLayer.Children.Add(ring);
        }

        var bubble = TourBubble(step, _tourIndex, steps.Count);
        bubble.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = bubble.DesiredSize;

        // Below the element when there is room, otherwise above, otherwise in the middle
        double x, y;
        if (hole.IsEmpty)
        {
            x = (w - size.Width) / 2;
            y = (h - size.Height) / 2;
        }
        else
        {
            x = Math.Clamp(hole.X, 16, Math.Max(16, w - size.Width - 16));
            y = hole.Bottom + 12 + size.Height <= h - 16 ? hole.Bottom + 12
              : hole.Top - 12 - size.Height >= 16       ? hole.Top - 12 - size.Height
              : Math.Max(16, (h - size.Height) / 2);
        }
        Canvas.SetLeft(bubble, x);
        Canvas.SetTop(bubble, y);
        TutorialLayer.Children.Add(bubble);
    }

    private FrameworkElement TourBubble(TourStep step, int index, int count)
    {
        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = $"{index + 1} VON {count}", FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_accent)
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = step.Title, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6)
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = step.Text, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Opacity = 0.9
        });

        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bool last = index == count - 1;
        if (!last)
        {
            var skip = new WpfButton { Content = "Überspringen", Style = (Style)FindResource("SecondaryButton"), HorizontalAlignment = WpfHA.Left };
            skip.Click += (_, _) => EndTutorial();
            buttons.Children.Add(skip);
        }

        var nav = new StackPanel { Orientation = WpfOrientation.Horizontal };
        if (index > 0)
        {
            var back = new WpfButton { Content = "Zurück", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
            back.Click += (_, _) => { _tourIndex--; ShowTourStep(); };
            nav.Children.Add(back);
        }
        var next = new WpfButton { Content = last ? "Fertig" : "Weiter", Style = (Style)FindResource("PrimaryButton") };
        next.Click += (_, _) => { _tourIndex++; ShowTourStep(); };
        nav.Children.Add(next);
        Grid.SetColumn(nav, 1);
        buttons.Children.Add(nav);
        panel.Children.Add(buttons);

        next.Loaded += (_, _) => next.Focus();

        return new WpfBorder
        {
            Width = 340, Padding = new Thickness(20, 16, 20, 16), CornerRadius = new CornerRadius(12),
            Background = (Brush)FindResource("SystemControlBackgroundChromeMediumBrush"),
            BorderBrush = new SolidColorBrush(_accent), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35 },
            Child = panel
        };
    }

    /// <summary>Escape closes the tour or the dialog; arrows step through the tour.</summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_tourIndex >= 0)
        {
            if (e.Key == Key.Escape)     { EndTutorial(); e.Handled = true; }
            else if (e.Key == Key.Right) { _tourIndex++; ShowTourStep(); e.Handled = true; }
            else if (e.Key == Key.Left && _tourIndex > 0) { _tourIndex--; ShowTourStep(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape && DialogLayer.Visibility == Visibility.Visible)
        {
            CloseDialog();
            e.Handled = true;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Small helpers
    // ══════════════════════════════════════════════════════════════════════

    private static IEnumerable<GradeEntry> GradesOf(string subject)
        => AppState.Grades
            .Where(g => string.Equals(g.Subject, subject, StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => g.Date);

    private static Grid TableGrid(Thickness margin)
    {
        var grid = new Grid { Margin = margin };
        foreach (var w in _cols) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return grid;
    }

    private static void AddCell(Grid grid, int column, UIElement element)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static WpfTextBlock HeaderText(string text, bool right = false) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Opacity = 0.5,
        HorizontalAlignment = right ? WpfHA.Right : WpfHA.Left
    };

    private static WpfBorder Divider() => new()
    {
        Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80))
    };

    /// <summary>A grade as a small rounded chip: red below 4, with its weight when not 1.</summary>
    private UIElement GradeChip(GradeEntry g)
    {
        bool low = g.Value < GradeEntry.Sufficient;
        var text = FormatGrade(g.Value) + (g.Weight == 1 ? "" : g.Weight == 0.5 ? " ½" : $" ×{g.Weight.ToString("0.##", _deCH)}");
        return new WpfBorder
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(0, 2, 6, 2),
            Background = low ? new SolidColorBrush(Color.FromArgb(36, _danger.R, _danger.G, _danger.B))
                             : new SolidColorBrush(Color.FromArgb(22, 0x80, 0x80, 0x80)),
            ToolTip = $"{(string.IsNullOrWhiteSpace(g.Title) ? "Note" : g.Title)}, {g.Date.ToString("d. MMM yyyy", _deCH)}",
            Child = new WpfTextBlock
            {
                Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = low ? new SolidColorBrush(_danger) : Brush(0xE0)
            }
        };
    }

    /// <summary>The rounded subject grade as a coloured badge: green from 5, red below 4.</summary>
    private FrameworkElement GradeBadge(double grade, bool large)
    {
        var color = grade < GradeEntry.Sufficient ? _danger : grade >= 5 ? _success : _accent;
        return new WpfBorder
        {
            CornerRadius = new CornerRadius(large ? 10 : 7),
            Padding = large ? new Thickness(14, 6, 14, 8) : new Thickness(9, 2, 9, 3),
            HorizontalAlignment = WpfHA.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
            Child = new WpfTextBlock
            {
                Text = FormatGrade(grade), FontSize = large ? 22 : 14, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            }
        };
    }

    private WpfButton IconButton(string glyph, string tooltip)
    {
        var btn = new WpfButton
        {
            Content = new WpfTextBlock
            {
                Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13
            },
            ToolTip = tooltip, Style = RowButtonStyle(), Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(2, 0, 0, 0)
        };
        System.Windows.Automation.AutomationProperties.SetName(btn, tooltip);
        return btn;
    }

    private Style? _rowButtonStyle;

    /// <summary>Flat button with a hover fill, for table rows and icon buttons.</summary>
    private Style RowButtonStyle()
    {
        if (_rowButtonStyle is not null) return _rowButtonStyle;

        var bd = new FrameworkElementFactory(typeof(WpfBorder), "Bd");
        bd.SetValue(WpfBorder.BackgroundProperty, new TemplateBindingExtension(WpfControl.BackgroundProperty));
        bd.SetValue(WpfBorder.PaddingProperty, new TemplateBindingExtension(WpfControl.PaddingProperty));
        bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(8));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, WpfHA.Stretch);
        bd.AppendChild(cp);

        var template = new ControlTemplate(typeof(WpfButton)) { VisualTree = bd };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(WpfBorder.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x1A, 0x80, 0x80, 0x80)), "Bd"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(WpfBorder.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80)), "Bd"));
        template.Triggers.Add(focus);

        _rowButtonStyle = new Style(typeof(WpfButton));
        _rowButtonStyle.Setters.Add(new Setter(WpfControl.TemplateProperty, template));
        _rowButtonStyle.Setters.Add(new Setter(WpfControl.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
        _rowButtonStyle.Setters.Add(new Setter(WpfControl.ForegroundProperty, FindResource("SystemControlForegroundBaseHighBrush")));
        _rowButtonStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        _rowButtonStyle.Setters.Add(new Setter(WpfControl.HorizontalContentAlignmentProperty, WpfHA.Stretch));
        return _rowButtonStyle;
    }

    private UIElement DialogButtons(WpfButton primary)
    {
        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHA.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new WpfButton { Content = "Abbrechen", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => CloseDialog();
        buttons.Children.Add(cancel);
        primary.Margin = new Thickness(0);
        buttons.Children.Add(primary);
        return buttons;
    }

    private static WpfTextBlock ErrorText() => new()
    {
        Foreground = new SolidColorBrush(_danger), FontSize = 12, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 8), Visibility = Visibility.Collapsed
    };

    private static StackPanel Labeled(string label, FrameworkElement control, bool stretch = false)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, stretch ? 0 : 14, 14) };
        sp.Children.Add(new WpfTextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 5)
        });
        sp.Children.Add(control);
        return sp;
    }

    /// <summary>The theme's text colour with some transparency; 0xFF is fully opaque.</summary>
    private Brush Brush(byte alpha)
    {
        var baseBrush = (SolidColorBrush)FindResource("SystemControlForegroundBaseHighBrush");
        var c = baseBrush.Color;
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    /// <summary>Accepts "4.5" as well as "4,5", whatever the Windows locale says.</summary>
    private static bool TryParseNumber(string text, out double value)
        => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string FormatGrade(double grade) => grade.ToString("0.0#", _deCH);

    private static string SignedPoints(double points)
        => (points > 0 ? "+" : "") + points.ToString("0.#", _deCH);
}
