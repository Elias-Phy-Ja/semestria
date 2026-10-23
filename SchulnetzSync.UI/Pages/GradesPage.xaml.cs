using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchulnetzSync.Core.Grades;

// Pin down the WPF versions of names WinForms also uses
using WpfBorder      = System.Windows.Controls.Border;
using WpfButton      = System.Windows.Controls.Button;
using WpfComboBox    = System.Windows.Controls.ComboBox;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfHA          = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPage        = System.Windows.Controls.Page;
using WpfTextBlock   = System.Windows.Controls.TextBlock;
using WpfTextBox     = System.Windows.Controls.TextBox;

namespace SchulnetzSync.UI.Pages;

/// <summary>
/// Grades typed in by hand: one tile per subject, the promotion check against the rule the
/// user picked, a form for new grades and a calculator for what the next exam needs.
///
/// Built in code like the calendar, because every part depends on the same computed
/// standings and the page simply rebuilds itself after each change.
/// </summary>
public partial class GradesPage : WpfPage
{
    private static readonly CultureInfo _deCH = CultureInfo.GetCultureInfo("de-CH");

    private static readonly Color _success = Color.FromRgb(0x16, 0xA3, 0x4A);
    private static readonly Color _danger  = Color.FromRgb(0xDC, 0x26, 0x26);
    private static readonly Color _accent  = Color.FromRgb(0x5C, 0x6E, 0xF7);

    private string? _selectedSubject;
    private Guid?   _editingGradeId;
    private bool    _ruleEditorOpen;

    // Target calculator input; survives a rebuild of the page
    private double _targetGrade  = 4.0;
    private string _targetWeight = "1";

    public GradesPage()
    {
        InitializeComponent();
        Loaded   += (_, _) => { AppState.Changed += OnStateChanged; Refresh(); };
        Unloaded += (_, _) => AppState.Changed -= OnStateChanged;
    }

    private void OnStateChanged() => Dispatcher.Invoke(Refresh);

    // ══════════════════════════════════════════════════════════════════════
    // Rebuilding
    // ══════════════════════════════════════════════════════════════════════

    private void Refresh()
    {
        var standings = Grading.Summarize(AppState.Grades);

        if (_selectedSubject is null && standings.Count > 0)
            _selectedSubject = standings[0].Subject;

        TxtSummary.Text = standings.Count == 0
            ? "Trag deine Noten ein, dann siehst du Schnitt, Pluspunkte und ob die Promotion reicht."
            : $"Gesamtschnitt {Grading.OverallAverage(standings):0.00}  ·  " +
              $"Pluspunkte {SignedPoints(Grading.PlusPoints(standings))}  ·  " +
              $"{Count(AppState.Grades.Count, "Note", "Noten")} in {Count(standings.Count, "Fach", "Fächern")}";

        BuildPromotionCard(standings);
        BuildSubjectList(standings);

        RightPane.Children.Clear();
        if (_ruleEditorOpen)
        {
            RightPane.Children.Add(BuildRuleEditor());
            return;
        }

        var selected = standings.FirstOrDefault(s => s.Subject == _selectedSubject);
        if (selected is not null)
            RightPane.Children.Add(BuildSubjectCard(selected));
        RightPane.Children.Add(BuildGradeForm());
        RightPane.Children.Add(BuildTargetCalculator(standings));
    }

    // ── Promotion check ──────────────────────────────────────────────────────

    private void BuildPromotionCard(IReadOnlyList<SubjectStanding> standings)
    {
        PromotionCard.Children.Clear();
        var rule   = AppState.PromotionRule;
        var result = rule.Evaluate(standings);

        var (glyph, text, color) = result.Status switch
        {
            ConditionStatus.Met    => ("✓", "Promotion erreicht, Stand jetzt", _success),
            ConditionStatus.Failed => ("✕", "Promotion gefährdet", _danger),
            _                      => ("○", "Noch nicht alles beurteilbar", _accent)
        };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new WpfTextBlock
        {
            Text       = $"{glyph}  {text}",
            FontSize   = 17,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color)
        });
        title.Children.Add(new WpfTextBlock
        {
            Text     = $"Regel: {rule.Name}",
            FontSize = 12,
            Opacity  = 0.65,
            Margin   = new Thickness(0, 2, 0, 0)
        });
        header.Children.Add(title);

        var btnRule = new WpfButton
        {
            Content           = _ruleEditorOpen ? "Schliessen" : "Regel ändern",
            Style             = (Style)FindResource("SecondaryButton"),
            VerticalAlignment = VerticalAlignment.Top
        };
        btnRule.Click += (_, _) => { _ruleEditorOpen = !_ruleEditorOpen; _draft = null; Refresh(); };
        Grid.SetColumn(btnRule, 1);
        header.Children.Add(btnRule);
        PromotionCard.Children.Add(header);

        if (result.Conditions.Count == 0)
        {
            PromotionCard.Children.Add(new WpfTextBlock
            {
                Text  = "Diese Regel hat noch keine Bedingungen.",
                Style = (Style)FindResource("NoteText")
            });
            return;
        }

        foreach (var c in result.Conditions)
            PromotionCard.Children.Add(ConditionRow(c));
    }

    private static UIElement ConditionRow(ConditionResult c)
    {
        var (glyph, color) = c.Status switch
        {
            ConditionStatus.Met    => ("✓", _success),
            ConditionStatus.Failed => ("✕", _danger),
            _                      => ("○", Color.FromRgb(0x80, 0x80, 0x80))
        };

        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new WpfTextBlock
        {
            Text = glyph, FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color)
        });
        var label = new WpfTextBlock { Text = c.Label, FontSize = 13 };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        var detail = new WpfTextBlock { Text = c.Detail, FontSize = 13, Opacity = 0.7 };
        Grid.SetColumn(detail, 2);
        row.Children.Add(detail);
        return row;
    }

    // ── Subject tiles ────────────────────────────────────────────────────────

    private void BuildSubjectList(IReadOnlyList<SubjectStanding> standings)
    {
        SubjectList.Children.Clear();

        if (standings.Count == 0)
        {
            SubjectList.Children.Add(new WpfTextBlock
            {
                Text  = "Noch keine Noten. Rechts kannst du die erste eintragen.",
                Style = (Style)FindResource("NoteText")
            });
            return;
        }

        foreach (var s in standings)
            SubjectList.Children.Add(SubjectTile(s));
    }

    private UIElement SubjectTile(SubjectStanding s)
    {
        bool isSel = s.Subject == _selectedSubject;

        var tile = new WpfBorder
        {
            CornerRadius    = new CornerRadius(10),
            Padding         = new Thickness(14, 10, 14, 10),
            Margin          = new Thickness(0, 0, 0, 8),
            Cursor          = WpfCursors.Hand,
            BorderThickness = new Thickness(1),
            BorderBrush     = isSel ? new SolidColorBrush(_accent) : (Brush)FindResource("SystemControlForegroundBaseLowBrush"),
            Background      = isSel ? new SolidColorBrush(Color.FromArgb(28, _accent.R, _accent.G, _accent.B))
                                    : (Brush)FindResource("SystemControlBackgroundChromeMediumLowBrush")
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new WpfTextBlock { Text = s.Subject, FontSize = 15, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new WpfTextBlock
        {
            Text     = $"Ø {s.Average:0.00}  ·  {s.Count} {(s.Count == 1 ? "Note" : "Noten")}  ·  {SignedPoints(s.PlusPoints)}",
            FontSize = 11,
            Opacity  = 0.65
        });
        grid.Children.Add(left);

        var grade = new WpfTextBlock
        {
            Text              = FormatGrade(s.Grade),
            FontSize          = 22,
            FontWeight        = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = s.IsInsufficient ? new SolidColorBrush(_danger)
                                                 : (Brush)FindResource("SystemControlForegroundBaseHighBrush")
        };
        Grid.SetColumn(grade, 1);
        grid.Children.Add(grade);

        tile.Child = grid;
        tile.MouseLeftButtonUp += (_, e) =>
        {
            _selectedSubject = s.Subject;
            _editingGradeId  = null;
            _ruleEditorOpen  = false;
            Refresh();
            e.Handled = true;
        };
        return tile;
    }

    // ── One subject in detail ────────────────────────────────────────────────

    private UIElement BuildSubjectCard(SubjectStanding s)
    {
        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text       = $"{s.Subject}  ·  Fachnote {FormatGrade(s.Grade)}",
            FontSize   = 17,
            FontWeight = FontWeights.Bold
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text   = $"Genauer Schnitt {s.Average:0.000}, gerundet auf halbe Noten. Pluspunkte {SignedPoints(s.PlusPoints)}.",
            Style  = (Style)FindResource("NoteText"),
            Margin = new Thickness(0, 2, 0, 12)
        });

        foreach (var g in AppState.Grades
                     .Where(g => string.Equals(g.Subject, s.Subject, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(g => g.Date))
            panel.Children.Add(GradeRow(g));

        return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
    }

    private UIElement GradeRow(GradeEntry g)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new WpfTextBlock
        {
            Text = g.Date.ToString("d. MMM yy", _deCH), FontSize = 12, Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center
        });

        var title = new WpfTextBlock
        {
            Text = string.IsNullOrWhiteSpace(g.Title) ? "Note" : g.Title, FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        var value = new WpfTextBlock
        {
            Text = FormatGrade(g.Value) + (g.Weight == 1 ? "" : $"  ×{g.Weight.ToString("0.##", _deCH)}"),
            FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = g.Value < GradeEntry.Sufficient ? new SolidColorBrush(_danger)
                                                         : (Brush)FindResource("SystemControlForegroundBaseHighBrush")
        };
        Grid.SetColumn(value, 2);
        row.Children.Add(value);

        var actions = new StackPanel { Orientation = WpfOrientation.Horizontal };
        var btnEdit = SmallButton("✏️", "Bearbeiten");
        btnEdit.Click += (_, _) => { _editingGradeId = g.Id; Refresh(); };
        var btnDelete = SmallButton("🗑", "Löschen");
        btnDelete.Click += (_, _) =>
        {
            var name = string.IsNullOrWhiteSpace(g.Title) ? $"die {FormatGrade(g.Value)} in {g.Subject}" : $"«{g.Title}»";
            if (MessageBox.Show($"{name} wirklich löschen?", "Note löschen",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (_editingGradeId == g.Id) _editingGradeId = null;
            AppState.RemoveGrade(g.Id);
        };
        actions.Children.Add(btnEdit);
        actions.Children.Add(btnDelete);
        Grid.SetColumn(actions, 3);
        row.Children.Add(actions);
        return row;
    }

    // ── Adding and editing a grade ───────────────────────────────────────────

    private UIElement BuildGradeForm()
    {
        var editing = _editingGradeId is Guid id ? AppState.Grades.FirstOrDefault(g => g.Id == id) : null;

        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = editing is null ? "NOTE EINTRAGEN" : "NOTE BEARBEITEN",
            Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 0, 0, 10)
        });

        var cmbSubject = new WpfComboBox { IsEditable = true, Height = 34, FontSize = 13, MinWidth = 120 };
        foreach (var code in AppState.GradeSubjectSuggestions()) cmbSubject.Items.Add(code);
        cmbSubject.Text = editing?.Subject ?? _selectedSubject ?? "";

        var txtValue  = new WpfTextBox { Height = 34, FontSize = 13, Width = 80, Text = editing is null ? "" : FormatGrade(editing.Value) };
        var txtWeight = new WpfTextBox { Height = 34, FontSize = 13, Width = 80, Text = (editing?.Weight ?? 1).ToString("0.##", _deCH) };
        var txtTitle  = new WpfTextBox { Height = 34, FontSize = 13, Text = editing?.Title ?? "" };
        var dpDate    = new DatePicker { Height = 34, FontSize = 13, Width = 150,
                                         SelectedDate = (editing?.Date ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue) };

        var fields = new WrapPanel();
        fields.Children.Add(Labeled("Fach", cmbSubject));
        fields.Children.Add(Labeled("Note", txtValue));
        fields.Children.Add(Labeled("Gewicht", txtWeight));
        fields.Children.Add(Labeled("Datum", dpDate));
        panel.Children.Add(fields);
        panel.Children.Add(Labeled("Wofür (optional)", txtTitle, stretch: true));

        var error = new WpfTextBlock
        {
            Foreground = new SolidColorBrush(_danger), FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed
        };
        panel.Children.Add(error);

        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal };
        var btnSave = new WpfButton
        {
            Content = editing is null ? "Note speichern" : "Änderungen speichern",
            Style   = (Style)FindResource("PrimaryButton"),
            Margin  = new Thickness(0, 0, 8, 0)
        };
        btnSave.Click += (_, _) =>
        {
            var problem = TrySave(editing, cmbSubject.Text, txtValue.Text, txtWeight.Text, txtTitle.Text, dpDate.SelectedDate);
            if (problem is null) return;
            error.Text       = problem;
            error.Visibility = Visibility.Visible;
        };
        buttons.Children.Add(btnSave);

        if (editing is not null)
        {
            var btnCancel = new WpfButton { Content = "Abbrechen", Style = (Style)FindResource("SecondaryButton") };
            btnCancel.Click += (_, _) => { _editingGradeId = null; Refresh(); };
            buttons.Children.Add(btnCancel);
        }
        panel.Children.Add(buttons);

        // Enter in any box saves, like on the task page
        foreach (var box in new UIElement[] { cmbSubject, txtValue, txtWeight, txtTitle })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) btnSave.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent)); };

        return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
    }

    /// <summary>Validates and stores the form. Returns an error message, or null on success.</summary>
    private string? TrySave(GradeEntry? editing, string subject, string value, string weight, string title, DateTime? date)
    {
        subject = subject.Trim().ToUpperInvariant();
        if (subject.Length == 0)
            return "Wähle oder tippe ein Fach, zum Beispiel INF.";
        if (!TryParseNumber(value, out var v) || v < GradeEntry.Min || v > GradeEntry.Max)
            return "Die Note muss zwischen 1 und 6 liegen, zum Beispiel 4.5.";
        if (!TryParseNumber(weight, out var w) || w <= 0)
            return "Das Gewicht muss grösser als 0 sein, zum Beispiel 1 oder 0.5.";

        var entry = new GradeEntry(
            Id:      editing?.Id ?? Guid.NewGuid(),
            Subject: subject,
            Value:   v,
            Weight:  w,
            Title:   title.Trim(),
            Date:    DateOnly.FromDateTime(date ?? DateTime.Today));

        _selectedSubject = subject;
        _editingGradeId  = null;
        AppState.SaveGrade(entry);   // fires Notify, which rebuilds the page
        return null;
    }

    // ── Target calculator ────────────────────────────────────────────────────

    private UIElement BuildTargetCalculator(IReadOnlyList<SubjectStanding> standings)
    {
        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = "ZIELRECHNER", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 0, 0, 4)
        });

        if (_selectedSubject is null)
        {
            panel.Children.Add(new WpfTextBlock
            {
                Text  = "Sobald ein Fach Noten hat, rechnet die App hier aus, was die nächste Prüfung braucht.",
                Style = (Style)FindResource("NoteText")
            });
            return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
        }

        panel.Children.Add(new WpfTextBlock
        {
            Text   = $"Welche Note brauchst du in der nächsten {_selectedSubject}-Prüfung?",
            Style  = (Style)FindResource("BodyText"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        var cmbTarget = new WpfComboBox { Height = 34, FontSize = 13, Width = 90 };
        for (double t = 3.5; t <= 6.0; t += 0.5) cmbTarget.Items.Add(FormatGrade(t));
        cmbTarget.SelectedItem = FormatGrade(_targetGrade);

        var txtWeight = new WpfTextBox { Height = 34, FontSize = 13, Width = 80, Text = _targetWeight };

        var fields = new WrapPanel();
        fields.Children.Add(Labeled("Ziel-Fachnote", cmbTarget));
        fields.Children.Add(Labeled("Gewicht der Prüfung", txtWeight));
        panel.Children.Add(fields);

        var result = new WpfTextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(result);

        var standing = standings.FirstOrDefault(s => s.Subject == _selectedSubject);

        void Update()
        {
            if (cmbTarget.SelectedItem is string t && TryParseNumber(t, out var target)) _targetGrade = target;
            _targetWeight = txtWeight.Text;

            if (!TryParseNumber(_targetWeight, out var w) || w <= 0)
            {
                result.Text       = "Gewicht muss grösser als 0 sein.";
                result.Foreground = new SolidColorBrush(_danger);
                return;
            }

            double needed = Grading.NeededGrade(standing, w, _targetGrade);
            // Grades are written to a tenth, so round the requirement up to the next one
            double shown  = Math.Ceiling(Math.Round(needed * 10, 6)) / 10;

            (result.Text, result.Foreground) = needed switch
            {
                <= GradeEntry.Min => ($"Sicher: Selbst mit einer 1 bleibt {_selectedSubject} bei mindestens {FormatGrade(_targetGrade)}.",
                                      (Brush)new SolidColorBrush(_success)),
                > GradeEntry.Max  => ($"Mit einer Prüfung nicht erreichbar, du bräuchtest eine {shown.ToString("0.0", _deCH)}.",
                                      (Brush)new SolidColorBrush(_danger)),
                _                 => ($"Du brauchst mindestens eine {shown.ToString("0.0", _deCH)}.",
                                      (Brush)FindResource("SystemControlForegroundBaseHighBrush"))
            };
        }

        cmbTarget.SelectionChanged += (_, _) => Update();
        txtWeight.TextChanged      += (_, _) => Update();
        Update();

        return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
    }

    // ══════════════════════════════════════════════════════════════════════
    // Rule editor
    // ══════════════════════════════════════════════════════════════════════

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

    private UIElement BuildRuleEditor()
    {
        if (_draft is null) LoadDraft(AppState.PromotionRule);

        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = "PROMOTIONSREGEL", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 0, 0, 10)
        });

        // Templates
        panel.Children.Add(new WpfTextBlock { Text = "Vorlage", Style = (Style)FindResource("LabelText") });
        var templates = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        templates.Children.Add(TemplateButton("System 1: Pluspunkte", PromotionRule.PlusPointsSystem));
        templates.Children.Add(TemplateButton("System 2: Gesamtschnitt", PromotionRule.AverageSystem));
        templates.Children.Add(TemplateButton("Leer beginnen", new PromotionRule("Eigene Regel", [])));
        panel.Children.Add(templates);

        var txtName = new WpfTextBox { Height = 34, FontSize = 13, Text = _draftName ?? "" };
        txtName.TextChanged += (_, _) => _draftName = txtName.Text;
        panel.Children.Add(Labeled("Name", txtName, stretch: true));

        panel.Children.Add(new WpfTextBlock { Text = "Bedingungen, alle müssen erfüllt sein", Style = (Style)FindResource("LabelText") });
        if (_draft!.Count == 0)
            panel.Children.Add(new WpfTextBlock { Text = "Noch keine. Füge unten welche hinzu.", Style = (Style)FindResource("NoteText") });
        foreach (var d in _draft)
            panel.Children.Add(DraftRow(d));

        // Adding conditions
        panel.Children.Add(new WpfTextBlock { Text = "Hinzufügen", Style = (Style)FindResource("LabelText"), Margin = new Thickness(0, 14, 0, 4) });
        var adders = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        adders.Children.Add(AddButton("+ Pluspunkte",          new DraftCondition { Kind = "plus",    Value = "0" }));
        adders.Children.Add(AddButton("+ Ungenügende Fächer",  new DraftCondition { Kind = "maxInsu", Value = "2" }));
        adders.Children.Add(AddButton("+ Gesamtschnitt",       new DraftCondition { Kind = "average", Value = "4" }));
        adders.Children.Add(AddButton("+ Mindestnote in Fach", new DraftCondition { Kind = "subject", Value = "4", Subject = _selectedSubject ?? "" }));
        panel.Children.Add(adders);

        var error = new WpfTextBlock
        {
            Foreground = new SolidColorBrush(_danger), FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed
        };
        panel.Children.Add(error);

        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal };
        var btnSave = new WpfButton { Content = "Regel speichern", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        btnSave.Click += (_, _) =>
        {
            if (TryBuildRule(out var rule, out var problem))
            {
                _ruleEditorOpen = false;
                _draft = null;
                AppState.PromotionRule = rule;   // fires Notify, which rebuilds the page
                return;
            }
            error.Text       = problem;
            error.Visibility = Visibility.Visible;
        };
        var btnCancel = new WpfButton { Content = "Abbrechen", Style = (Style)FindResource("SecondaryButton") };
        btnCancel.Click += (_, _) => { _ruleEditorOpen = false; _draft = null; Refresh(); };
        buttons.Children.Add(btnSave);
        buttons.Children.Add(btnCancel);
        panel.Children.Add(buttons);

        return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
    }

    private WpfButton TemplateButton(string label, PromotionRule template)
    {
        var btn = new WpfButton { Content = label, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 8) };
        btn.Click += (_, _) => { LoadDraft(template); Refresh(); };
        return btn;
    }

    private WpfButton AddButton(string label, DraftCondition condition)
    {
        var btn = new WpfButton { Content = label, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 8) };
        btn.Click += (_, _) => { _draft!.Add(condition); Refresh(); };
        return btn;
    }

    private UIElement DraftRow(DraftCondition d)
    {
        var row = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

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

        var btnRemove = SmallButton("✕", "Bedingung entfernen");
        btnRemove.Click += (_, _) => { _draft!.Remove(d); Refresh(); };
        row.Children.Add(btnRemove);
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
    // Small helpers
    // ══════════════════════════════════════════════════════════════════════

    private static StackPanel Labeled(string label, FrameworkElement control, bool stretch = false)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, stretch ? 0 : 12, 12) };
        sp.Children.Add(new WpfTextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 4)
        });
        sp.Children.Add(control);
        return sp;
    }

    private WpfButton SmallButton(string glyph, string tooltip) => new()
    {
        Content = glyph,
        ToolTip = tooltip,
        Style   = (Style)FindResource("SecondaryButton"),
        Padding = new Thickness(8, 3, 8, 3),
        Margin  = new Thickness(4, 0, 0, 0),
        HorizontalAlignment = WpfHA.Left
    };

    /// <summary>Accepts "4.5" as well as "4,5", whatever the Windows locale says.</summary>
    private static bool TryParseNumber(string text, out double value)
        => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string FormatGrade(double grade) => grade.ToString("0.0#", _deCH);

    private static string SignedPoints(double points)
        => (points > 0 ? "+" : "") + points.ToString("0.#", _deCH);
}
