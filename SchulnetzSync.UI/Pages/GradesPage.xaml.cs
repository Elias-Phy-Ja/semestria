using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Grades;
using SchulnetzSync.UI.Controls;

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
public partial class GradesPage : WpfPage, ITourPage
{
    private static readonly CultureInfo _deCH = CultureInfo.GetCultureInfo("de-CH");

    private static readonly Color _success = Color.FromRgb(0x16, 0xA3, 0x4A);
    private static readonly Color _danger  = Color.FromRgb(0xDC, 0x26, 0x26);
    private static readonly Color _accent  = Color.FromRgb(0x5C, 0x6E, 0xF7);
    private static readonly Color _neutral = Color.FromRgb(0x80, 0x80, 0x80);

    /// <summary>Below this page width the detail moves under the table.</summary>
    private const double NarrowWidth = 1000;

    private string? _selectedSubject;

    /// <summary>True while the page shows the final certificate (Matur / BM) instead of a period.</summary>
    private bool _finalMode;

    /// <summary>The period on screen; null means the running one.</summary>
    private Guid? _viewPeriodId;

    /// <summary>The grades of the period on screen, set by <see cref="Refresh"/>.</summary>
    private IReadOnlyList<GradeEntry> _viewGrades = [];

    /// <summary>The period on screen; null while no semester or year has been started.</summary>
    private Guid? ViewPeriodId
        => AppState.GradePeriods is not { } state ? null
         : _viewPeriodId is Guid id && state.Periods.Any(p => p.Id == id) ? id
         : state.CurrentId;

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
        };
        Unloaded    += (_, _) => AppState.Changed -= OnStateChanged;
        SizeChanged += (_, _) => ApplyLayoutWidth();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnStateChanged() => Dispatcher.Invoke(Refresh);

    // ══════════════════════════════════════════════════════════════════════
    // Rebuilding
    // ══════════════════════════════════════════════════════════════════════

    private void Refresh()
    {
        // No rule yet: the page asks for one before anything else
        bool setup = !AppState.HasChosenRule;
        SetupPane.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
        foreach (var part in new UIElement[] { KpiStrip, ConditionChips, MainArea, TabBar, PeriodBar, BtnAddGrade, BtnHelp })
            part.Visibility = setup ? Visibility.Collapsed : Visibility.Visible;
        if (setup)
        {
            BtnRule.Visibility      = Visibility.Collapsed;
            PeriodBanner.Visibility = Visibility.Collapsed;
            TxtSummary.Text         = "Bevor es losgeht: Wie rechnet deine Schule?";
            BuildSetup();
            return;
        }

        if (_finalMode && !AppState.HasFinal) _finalMode = false;
        BuildTabs();
        BtnRule.Visibility  = _finalMode ? Visibility.Collapsed : Visibility.Visible;
        BtnAddGrade.Content = _finalMode ? "+  Prüfung eintragen" : "+  Note eintragen";
        System.Windows.Automation.AutomationProperties.SetName(BtnAddGrade, _finalMode ? "Prüfung eintragen" : "Note eintragen");
        if (_finalMode) { RefreshFinal(); return; }

        _viewGrades   = ViewPeriodId is Guid viewId ? AppState.GradesIn(viewId) : [];
        var standings = Grading.Summarize(_viewGrades);
        var rule      = AppState.PromotionRule;
        var result    = rule.Evaluate(standings);

        if (_selectedSubject is null || standings.All(s => s.Subject != _selectedSubject))
            _selectedSubject = standings.FirstOrDefault()?.Subject;

        TxtSummary.Text = standings.Count == 0
            ? "Trag deine Noten ein. Semestria rechnet Schnitte, Pluspunkte und sagt dir, ob die Promotion reicht."
            : $"{Count(_viewGrades.Count, "Note", "Noten")} in {Count(standings.Count, "Fach", "Fächern")}  ·  " +
              "Fachnoten auf halbe Noten gerundet";
        BtnRule.Content = $"Regel: {rule.Name}";

        BuildPeriodBar();
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

    // ── Tabs: semesters, school years and the final certificate ──────────────

    /// <summary>
    /// One tab per semester or school year, «+ Semester / Jahr», and on the right, set apart,
    /// the final certificate: «+ Abschluss hinzufügen» until there is one.
    /// </summary>
    private void BuildTabs()
    {
        PeriodTabs.Children.Clear();
        FinalTab.Children.Clear();
        var state = AppState.GradePeriods;

        foreach (var p in (state?.Periods ?? []).OrderBy(p => p.StartedAt))
        {
            bool selected = !_finalMode && p.Id == ViewPeriodId;
            var tab = new WpfButton
            {
                Content = p.Id == state!.CurrentId ? $"{p.Name}  ·  aktuell" : p.Name,
                Style   = (Style)FindResource(selected ? "PrimaryButton" : "SecondaryButton"),
                Margin  = new Thickness(0, 0, 8, 8)
            };
            System.Windows.Automation.AutomationProperties.SetName(tab, $"Tab {p.Name}");
            tab.Click += (_, _) =>
            {
                _finalMode       = false;
                _viewPeriodId    = p.Id == state!.CurrentId ? null : p.Id;
                _selectedSubject = null;
                Refresh();
            };
            PeriodTabs.Children.Add(tab);
        }

        // Without any period this is the first thing to do, so it looks like it
        var add = state is null
            ? new WpfButton
              {
                  Content = "+  Semester / Jahr starten", Style = (Style)FindResource("SecondaryButton"),
                  Margin = new Thickness(0, 0, 8, 8)
              }
            : new WpfButton
              {
                  Content = "+  Semester / Jahr", Style = RowButtonStyle(), Padding = new Thickness(12, 8, 12, 8),
                  Margin = new Thickness(0, 0, 8, 8)
              };
        add.ToolTip = "Ein neues Semester oder Schuljahr beginnen";
        System.Windows.Automation.AutomationProperties.SetName(add, "Neues Semester oder Jahr");
        add.Click += (_, _) => ShowNewPeriodDialog();
        PeriodTabs.Children.Add(add);
        _addPeriodButton = add;

        // The final certificate sits apart on the right
        FinalTab.Children.Add(new WpfBorder
        {
            Width = 1, Margin = new Thickness(0, 4, 16, 12),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80))
        });
        if (AppState.HasFinal)
        {
            var name = AppState.FinalExams.Method == FinalMethod.KantiMatur ? "Matur" : "BM";
            var tab = new WpfButton
            {
                Content = $"🎓  Abschluss · {name}",
                Style   = (Style)FindResource(_finalMode ? "PrimaryButton" : "SecondaryButton"),
                Margin  = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Automation.AutomationProperties.SetName(tab, "Tab Abschluss");
            tab.Click += (_, _) => { _finalMode = true; _selectedSubject = null; Refresh(); };
            FinalTab.Children.Add(tab);
        }
        else
        {
            var addFinal = new WpfButton
            {
                Content = "🎓  Abschluss hinzufügen", Style = (Style)FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Matur oder Berufsmatur: Abschlussnoten aus Zeugnis- und Prüfungsnoten"
            };
            System.Windows.Automation.AutomationProperties.SetName(addFinal, "Abschluss hinzufügen");
            addFinal.Click += (_, _) => ShowAddFinalDialog();
            FinalTab.Children.Add(addFinal);
        }
    }

    /// <summary>«+ Semester / Jahr», for the hint that points at it.</summary>
    private WpfButton? _addPeriodButton;

    /// <summary>
    /// Adding a grade without a running period: a bubble at «+ Semester / Jahr» says why
    /// and offers to start one.
    /// </summary>
    private void ShowPeriodHint()
    {
        CloseDialog();
        MainWindow.Current?.Tour.ShowHint(
            new TourStep(() => _addPeriodButton, "Zuerst ein Semester oder Schuljahr",
                "Noten gehören immer zu einem Semester oder Schuljahr. Starte es hier, danach kannst du eintragen."),
            "Jetzt starten", ShowNewPeriodDialog);
    }

    private void ShowAddFinalDialog()
    {
        OpenDialog("Abschluss hinzufügen",
            "Semestria rechnet deine Abschlussnoten aus den Zeugnissen deiner Semester oder Jahre und den Prüfungsnoten, " +
            "so wie es deine Schule macht.");

        var cards = new UniformGrid { Columns = 2 };
        foreach (var (title, text, method) in new[]
        {
            ("Kanti Aarau · Matur",
             "Vorschlagsnote aus dem letzten Jahreszeugnis, Prüfung schriftlich und mündlich, Maturnote auf halbe Noten.",
             FinalMethod.KantiMatur),
            ("IMS Aarau · BM",
             "Erfahrungsnote aus allen Semesterzeugnissen, Prüfung schriftlich und mündlich, Fachnote auf halbe Noten.",
             FinalMethod.ImsBm)
        })
        {
            var panel = new StackPanel();
            panel.Children.Add(new WpfTextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new WpfTextBlock { Text = text, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            var card = new WpfButton
            {
                Content = panel, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 8),
                HorizontalContentAlignment = WpfHA.Left, VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(14, 12, 14, 12)
            };
            System.Windows.Automation.AutomationProperties.SetName(card, $"Abschluss {title}");
            card.Click += (_, _) =>
            {
                CloseDialog();
                _finalMode       = true;
                _selectedSubject = null;
                AppState.AddFinal(method);   // fires Notify, which rebuilds the page
            };
            cards.Children.Add(card);
        }
        DialogContent.Children.Add(cards);

        var cancel = new WpfButton { Content = "Abbrechen", Style = (Style)FindResource("SecondaryButton"), HorizontalAlignment = WpfHA.Right, Margin = new Thickness(0, 8, 0, 0) };
        cancel.Click += (_, _) => CloseDialog();
        DialogContent.Children.Add(cancel);
    }

    /// <summary>First visit: choose how the school computes, before any grade.</summary>
    private void BuildSetup()
    {
        SetupPane.Children.Clear();
        SetupPane.Children.Add(new WpfTextBlock
        {
            Text = "Wähle zuerst die Regel deiner Schule. Danach kannst du Noten eintragen. " +
                   "Ändern geht später jederzeit über «Regel».",
            FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18), MaxWidth = 760,
            HorizontalAlignment = WpfHA.Left
        });

        var schools = new UniformGrid { Columns = 2, MaxWidth = 900, HorizontalAlignment = WpfHA.Left };
        schools.Children.Add(SetupCard("Kanti Aarau · Matur", "KANTI-STANDARD",
            "Pluspunkte mindestens 0, wobei Minuspunkte doppelt zählen, und höchstens 4 Noten unter 4.",
            PromotionRule.KantiMatur));
        schools.Children.Add(SetupCard("IMS Aarau · BM", "IMS-STANDARD",
            "Gesamtnote mindestens 4.0, höchstens 2 ungenügend, höchstens 2 Minuspunkte und Informatik mindestens 4.",
            PromotionRule.ImsBm));
        SetupPane.Children.Add(new WpfTextBlock { Text = "VORLAGE DEINER SCHULE", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 0, 0, 8) });
        SetupPane.Children.Add(schools);

        var generic = new UniformGrid { Columns = 3, MaxWidth = 900, HorizontalAlignment = WpfHA.Left };
        generic.Children.Add(SetupCard("Pluspunkte", null, "Pluspunkte mindestens 0, höchstens 2 Fächer ungenügend.", PromotionRule.PlusPointsSystem));
        generic.Children.Add(SetupCard("Gesamtschnitt", null, "Schnitt mindestens 4, höchstens 2 Fächer ungenügend.", PromotionRule.AverageSystem));
        generic.Children.Add(SetupCard("Eigene Regel", null, "Selbst zusammenstellen aus Pluspunkten, Schnitt, Minuspunkten und Mindestnoten.", null));
        SetupPane.Children.Add(new WpfTextBlock { Text = "ANDERE SCHULE", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 12, 0, 8) });
        SetupPane.Children.Add(generic);
    }

    private UIElement SetupCard(string title, string? badge, string text, PromotionRule? rule)
    {
        var panel = new StackPanel();
        if (badge is not null)
            panel.Children.Add(new WpfBorder
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = WpfHA.Left,
                Background = new SolidColorBrush(Color.FromArgb(40, _accent.R, _accent.G, _accent.B)),
                Child = new WpfTextBlock { Text = badge, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(_accent) }
            });
        panel.Children.Add(new WpfTextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new WpfTextBlock { Text = text, FontSize = 13, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });

        var card = new WpfButton
        {
            Content = panel, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 12, 12),
            HorizontalContentAlignment = WpfHA.Left, VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(18, 16, 18, 16), MinHeight = 120
        };
        System.Windows.Automation.AutomationProperties.SetName(card, $"Regel {title} wählen");
        card.Click += (_, _) =>
        {
            if (rule is null)
            {
                // Own rule: the editor; saving it counts as choosing
                LoadDraft(new PromotionRule("Eigene Regel", []));
                ShowRuleDialog();
                return;
            }
            AppState.PromotionRule = rule;   // fires Notify, which rebuilds the page
            MainWindow.Current?.MaybeStartTour(this);
        };
        return card;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Final certificate: Matur (Kanti) or Berufsmatur (IMS)
    // ══════════════════════════════════════════════════════════════════════

    private IReadOnlyList<FinalSubject> _finals = [];

    private void RefreshFinal()
    {
        var state     = AppState.FinalExams;
        _finals       = FinalGrades.Compute(state.Method, AppState.ReportCards(), state.Exams);
        var standings = FinalGrades.AsStandings(_finals);
        var rule      = FinalGrades.RuleFor(state.Method);
        var result    = rule.Evaluate(standings);

        if (_selectedSubject is null || _finals.All(f => f.Subject != _selectedSubject))
            _selectedSubject = _finals.FirstOrDefault()?.Subject;

        int incomplete = _finals.Count(f => !f.IsComplete);
        TxtSummary.Text = _finals.Count == 0
            ? "Hier rechnet Semestria deine Abschlussnoten aus Zeugnis- und Prüfungsnoten."
            : $"{Count(_finals.Count, "Fach", "Fächer")}  ·  {Count(state.Exams.Count, "Prüfung", "Prüfungen")} eingetragen" +
              (incomplete > 0 ? $"  ·  {incomplete} ohne Zeugnisnote" : "");

        PeriodBanner.Visibility = Visibility.Collapsed;
        BuildMethodBar(state.Method);
        BuildKpis(standings, result, rule);
        BuildConditionChips(result);
        BuildFinalTable(state.Method);
        BuildFinalDetail(state.Method);
        ApplyLayoutWidth();
    }

    private void BuildMethodBar(FinalMethod method)
    {
        PeriodBar.Children.Clear();
        foreach (var (label, m) in new[] { ("Kanti Aarau · Matur", FinalMethod.KantiMatur), ("IMS Aarau · BM", FinalMethod.ImsBm) })
        {
            var btn = new WpfButton
            {
                Content = label, Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)FindResource(m == method ? "PrimaryButton" : "SecondaryButton")
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, $"Rechnung {label}");
            btn.Click += (_, _) => { if (m != method) AppState.SetFinalMethod(m); };
            PeriodBar.Children.Add(btn);
        }
        PeriodBar.Children.Add(new WpfTextBlock
        {
            Text = method == FinalMethod.KantiMatur
                ? "Vorschlagsnote = Zeugnis des letzten Jahres, Prüfung = Mittel schriftlich/mündlich, Maturnote = Mittel, auf halbe Noten gerundet."
                : "Erfahrungsnote = Mittel aller Semesterzeugnisse (Zehntel), Prüfung = Mittel schriftlich/mündlich (Zehntel), Fachnote = Mittel, auf halbe Noten gerundet.",
            FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, MaxWidth = 560,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 6, 0, 0)
        });

        var remove = new WpfButton
        {
            Content = "Abschluss entfernen", Style = RowButtonStyle(), Padding = new Thickness(10, 6, 10, 6),
            Foreground = new SolidColorBrush(_danger), Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Den Abschluss mit allen eingetragenen Prüfungsnoten entfernen"
        };
        System.Windows.Automation.AutomationProperties.SetName(remove, "Abschluss entfernen");
        remove.Click += (_, _) =>
        {
            int exams = AppState.FinalExams.Exams.Count;
            var text = exams == 0
                ? "Abschluss entfernen? Deine Semester- und Jahresnoten bleiben."
                : $"Abschluss mit {Count(exams, "Prüfung", "Prüfungen")} entfernen? Deine Semester- und Jahresnoten bleiben.";
            if (MessageBox.Show(text, "Abschluss entfernen", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _finalMode = false;
            AppState.RemoveFinal();
        };
        PeriodBar.Children.Add(remove);
    }

    private static string BaseName(FinalMethod method)
        => method == FinalMethod.KantiMatur ? "Vorschlagsnote" : "Erfahrungsnote";

    private static string FinalName(FinalMethod method)
        => method == FinalMethod.KantiMatur ? "Maturnote" : "Fachnote";

    // Columns of the final table: subject, base, exam, final
    private static readonly GridLength[] _finalCols =
    [
        new(72), new(1, GridUnitType.Star), new(190), new(86)
    ];

    private void BuildFinalTable(FinalMethod method)
    {
        SubjectTable.Children.Clear();

        if (_finals.Count == 0)
        {
            var panel = new StackPanel { Margin = new Thickness(32, 40, 32, 44), HorizontalAlignment = WpfHA.Center };
            panel.Children.Add(new WpfTextBlock
            {
                Text = "Noch nichts zum Rechnen", FontSize = 18, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = WpfHA.Center, Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new WpfTextBlock
            {
                Text = $"Die {BaseName(method)} kommt aus deinen Semestern und Jahren. Trag dort Noten ein, " +
                       "oder füge hier direkt eine Prüfung mit eigenem Wert hinzu.",
                FontSize = 13, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                MaxWidth = 380, HorizontalAlignment = WpfHA.Center
            });
            var add = new WpfButton
            {
                Content = "+  Prüfung eintragen", Style = (Style)FindResource("PrimaryButton"),
                HorizontalAlignment = WpfHA.Center, Margin = new Thickness(0, 18, 0, 0)
            };
            add.Click += (_, _) => ShowExamDialog(null);
            panel.Children.Add(add);
            SubjectTable.Children.Add(panel);
            return;
        }

        var header = FinalGrid(new Thickness(18, 14, 18, 10));
        AddCell(header, 0, HeaderText("FACH"));
        AddCell(header, 1, HeaderText(BaseName(method).ToUpperInvariant()));
        AddCell(header, 2, HeaderText("PRÜFUNG", right: true));
        AddCell(header, 3, HeaderText(FinalName(method).ToUpperInvariant(), right: true));
        SubjectTable.Children.Add(header);
        SubjectTable.Children.Add(Divider());

        foreach (var f in _finals)
            SubjectTable.Children.Add(FinalRow(f, method));
    }

    private static Grid FinalGrid(Thickness margin)
    {
        var grid = new Grid { Margin = margin };
        foreach (var w in _finalCols) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return grid;
    }

    private UIElement FinalRow(FinalSubject f, FinalMethod method)
    {
        bool isSel = f.Subject == _selectedSubject;
        var grid = FinalGrid(new Thickness(0));

        AddCell(grid, 0, new WpfTextBlock
        {
            Text = f.Subject, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });

        var basePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        basePanel.Children.Add(new WpfTextBlock
        {
            Text = f.Base is double b ? FormatTenth(b) : "–", FontSize = 14, FontWeight = FontWeights.SemiBold
        });
        basePanel.Children.Add(new WpfTextBlock
        {
            Text = f.BaseSource, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis
        });
        AddCell(grid, 1, basePanel);

        var exam = AppState.FinalExams.Exams.FirstOrDefault(e => string.Equals(e.Subject, f.Subject, StringComparison.OrdinalIgnoreCase));
        var examPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = WpfHA.Right };
        examPanel.Children.Add(new WpfTextBlock
        {
            Text = f.Exam is double p ? FormatExam(p) : "keine", FontSize = 14,
            FontWeight = f.Exam is null ? FontWeights.Normal : FontWeights.SemiBold,
            Opacity = f.Exam is null ? 0.5 : 1, HorizontalAlignment = WpfHA.Right
        });
        if (ExamParts(exam) is { Length: > 0 } parts)
            examPanel.Children.Add(new WpfTextBlock
            {
                Text = parts, FontSize = 11, Opacity = 0.6, HorizontalAlignment = WpfHA.Right
            });
        AddCell(grid, 2, examPanel);

        AddCell(grid, 3, f.Final is double fin
            ? GradeBadge(fin, large: false)
            : new WpfTextBlock { Text = "offen", Opacity = 0.5, HorizontalAlignment = WpfHA.Right, VerticalAlignment = VerticalAlignment.Center });

        var row = new WpfButton
        {
            Content = grid, Style = RowButtonStyle(), Padding = new Thickness(18, 10, 18, 10),
            Background = isSel ? new SolidColorBrush(Color.FromArgb(30, _accent.R, _accent.G, _accent.B))
                               : System.Windows.Media.Brushes.Transparent,
            ToolTip = $"{f.Subject}: Details und Prüfungsziel"
        };
        System.Windows.Automation.AutomationProperties.SetName(row, $"Abschluss {f.Subject}");
        row.Click += (_, _) => { _selectedSubject = f.Subject; Refresh(); };
        return row;
    }

    /// <summary>"schriftlich 4 · mündlich 4.5", or empty when there is no exam.</summary>
    private static string ExamParts(ExamEntry? exam)
    {
        if (exam is null) return "";
        var parts = new List<string>();
        if (exam.Written is double w) parts.Add($"schriftlich {FormatGrade(w)}");
        if (exam.Oral    is double o) parts.Add($"mündlich {FormatGrade(o)}");
        return string.Join("  ·  ", parts);
    }

    private void BuildFinalDetail(FinalMethod method)
    {
        DetailPane.Children.Clear();
        var f = _finals.FirstOrDefault(x => x.Subject == _selectedSubject);
        if (f is null)
        {
            _calculatorCard = new WpfBorder
            {
                Style = (Style)FindResource("Card"),
                Child = new WpfTextBlock
                {
                    Text = "Wähle ein Fach, dann siehst du, wie die Note entsteht und was du in der Prüfung brauchst.",
                    Style = (Style)FindResource("NoteText")
                }
            };
            DetailPane.Children.Add(_calculatorCard);
            return;
        }

        var exam  = AppState.FinalExams.Exams.FirstOrDefault(e => string.Equals(e.Subject, f.Subject, StringComparison.OrdinalIgnoreCase));
        var panel = new StackPanel();

        var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new WpfTextBlock { Text = f.Subject, FontSize = 20, FontWeight = FontWeights.Bold });
        title.Children.Add(new WpfTextBlock { Text = FinalName(method), FontSize = 12, Opacity = 0.65 });
        head.Children.Add(title);
        if (f.Final is double fin)
        {
            var badge = GradeBadge(fin, large: true);
            Grid.SetColumn(badge, 1);
            head.Children.Add(badge);
        }
        panel.Children.Add(head);

        panel.Children.Add(DetailLine(BaseName(method), f.Base is double b ? FormatTenth(b) : "fehlt", f.BaseSource));
        panel.Children.Add(DetailLine("Prüfungsnote", f.Exam is double p ? FormatExam(p) : "keine Prüfung",
            ExamParts(exam) is { Length: > 0 } parts ? parts : "nur die " + BaseName(method) + " zählt"));
        if (f.Unrounded is double u && f.Exam is not null)
            panel.Children.Add(DetailLine("Mittel", u.ToString("0.###", _deCH), $"auf halbe Noten gerundet: {FormatGrade(f.Final!.Value)}"));

        var edit = new WpfButton
        {
            Content = exam is null ? $"+  Prüfung in {f.Subject}" : "Prüfung bearbeiten",
            Style = (Style)FindResource("SecondaryButton"), HorizontalAlignment = WpfHA.Left, Margin = new Thickness(0, 12, 0, 0)
        };
        edit.Click += (_, _) => ShowExamDialog(f.Subject);
        panel.Children.Add(edit);
        DetailPane.Children.Add(new WpfBorder { Style = (Style)FindResource("Card"), Child = panel });

        _calculatorCard = BuildExamTarget(f, method);
        DetailPane.Children.Add(_calculatorCard);
    }

    private static UIElement DetailLine(string label, string value, string hint)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new WpfTextBlock { Text = label, FontSize = 13 });
        left.Children.Add(new WpfTextBlock { Text = hint, FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(left);
        var right = new WpfTextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>What the exam has to bring for a target final grade.</summary>
    private FrameworkElement BuildExamTarget(FinalSubject f, FinalMethod method)
    {
        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = "PRÜFUNGSZIEL", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 0, 0, 6)
        });

        if (f.Base is not double baseGrade)
        {
            panel.Children.Add(new WpfTextBlock
            {
                Text = $"Dafür braucht es eine {BaseName(method)}. Trag Noten in den Semestern ein oder einen eigenen Wert in der Prüfung.",
                Style = (Style)FindResource("NoteText")
            });
            return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
        }

        panel.Children.Add(new WpfTextBlock
        {
            Text = $"Welchen Prüfungsschnitt brauchst du in {f.Subject}?", FontSize = 13,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        var cmbTarget = new WpfComboBox { Height = 34, FontSize = 13, Width = 90 };
        for (double t = 3.5; t <= 6.0; t += 0.5) cmbTarget.Items.Add(FormatGrade(t));
        cmbTarget.SelectedItem = FormatGrade(_targetGrade);
        System.Windows.Automation.AutomationProperties.SetName(cmbTarget, $"Ziel-{FinalName(method)}");
        panel.Children.Add(Labeled($"Ziel-{FinalName(method)}", cmbTarget));

        var resultBox  = new WpfBorder { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 12) };
        var resultText = new WpfTextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        resultBox.Child = resultText;
        panel.Children.Add(resultBox);

        void Update()
        {
            if (cmbTarget.SelectedItem is string t && TryParseNumber(t, out var target)) _targetGrade = target;

            double needed = FinalGrades.NeededExam(method, baseGrade, _targetGrade);
            // Written and oral are half grades, so their mean moves in quarters
            double shown = Math.Ceiling(Math.Round(needed * 4, 6)) / 4;
            Color tone;
            (resultText.Text, tone) = needed switch
            {
                <= GradeEntry.Min => ($"Sicher: Selbst mit einer 1 in der Prüfung erreichst du eine {FormatGrade(_targetGrade)}.", _success),
                > GradeEntry.Max  => ($"Nicht erreichbar: Die Prüfung müsste im Schnitt {shown.ToString("0.0#", _deCH)} geben.", _danger),
                _                 => ($"Du brauchst in der Prüfung im Schnitt mindestens {shown.ToString("0.0#", _deCH)}.", _accent)
            };
            resultBox.Background  = new SolidColorBrush(Color.FromArgb(28, tone.R, tone.G, tone.B));
            resultText.Foreground = new SolidColorBrush(tone);
        }
        cmbTarget.SelectionChanged += (_, _) => Update();
        Update();

        panel.Children.Add(new WpfTextBlock
        {
            Text = "Schnitt aus schriftlicher und mündlicher Prüfung; mit nur einer Prüfung zählt diese.",
            Style = (Style)FindResource("NoteText"), Margin = new Thickness(0, 8, 0, 0)
        });
        return new WpfBorder { Style = (Style)FindResource("Card"), Child = panel };
    }

    private void ShowExamDialog(string? subject)
    {
        var state  = AppState.FinalExams;
        var method = state.Method;
        var exam   = subject is null ? null
                   : state.Exams.FirstOrDefault(e => string.Equals(e.Subject, subject, StringComparison.OrdinalIgnoreCase));
        var final  = subject is null ? null : _finals.FirstOrDefault(f => f.Subject == subject);

        OpenDialog(exam is null ? "Prüfung eintragen" : $"Prüfung in {subject}",
            "Prüfungsnoten in ganzen oder halben Noten. Leer lassen, was es nicht gibt.");

        var cmbSubject = new WpfComboBox { IsEditable = true, Height = 36, FontSize = 14, Width = 140 };
        foreach (var code in AppState.GradeSubjectSuggestions().Union(_finals.Select(f => f.Subject)).Distinct().OrderBy(c => c))
            cmbSubject.Items.Add(code);
        cmbSubject.Text = subject ?? "";
        cmbSubject.IsEnabled = exam is null;
        System.Windows.Automation.AutomationProperties.SetName(cmbSubject, "Fach");

        WpfTextBox Box(string name, double? value)
        {
            var box = new WpfTextBox { Height = 36, FontSize = 14, Width = 90, Text = value is double v ? FormatGrade(v) : "" };
            System.Windows.Automation.AutomationProperties.SetName(box, name);
            return box;
        }
        var txtWritten = Box("Schriftlich", exam?.Written);
        var txtOral    = Box("Mündlich", exam?.Oral);
        var txtOwn     = new WpfTextBox
        {
            Height = 36, FontSize = 14, Width = 90,
            Text = exam?.BaseOverride is double own ? own.ToString("0.##", _deCH) : ""
        };
        System.Windows.Automation.AutomationProperties.SetName(txtOwn, $"Eigene {BaseName(method)}");

        var top = new WrapPanel();
        top.Children.Add(Labeled("Fach", cmbSubject));
        top.Children.Add(Labeled("Schriftlich", txtWritten));
        top.Children.Add(Labeled("Mündlich", txtOral));
        DialogContent.Children.Add(top);

        var computed = final?.Base is double b && exam?.BaseOverride is null
            ? $"Berechnet: {FormatTenth(b)} ({final.BaseSource}). "
            : "";
        DialogContent.Children.Add(Labeled($"Eigene {BaseName(method)} (optional)", txtOwn));
        DialogContent.Children.Add(new WpfTextBlock
        {
            Text = computed + "Nur ausfüllen, wenn die Zeugnisse nicht in Semestria stehen oder für Sonderfälle wie IDPA/IDAF.",
            Style = (Style)FindResource("NoteText"), Margin = new Thickness(0, -8, 0, 12)
        });

        var error = ErrorText();
        DialogContent.Children.Add(error);

        var save = new WpfButton { Content = "Speichern", Style = (Style)FindResource("PrimaryButton") };
        void Save()
        {
            var code = cmbSubject.Text.Trim().ToUpperInvariant();
            string? problem = null;
            double? Parse(WpfTextBox box, bool examGrade)
            {
                if (string.IsNullOrWhiteSpace(box.Text)) return null;
                if (!TryParseNumber(box.Text, out var v) || v < GradeEntry.Min || v > GradeEntry.Max)
                { problem ??= "Noten müssen zwischen 1 und 6 liegen."; return null; }
                if (examGrade && !FinalGrades.IsValidExamGrade(v))
                { problem ??= "Prüfungsnoten gibt es nur ganz oder halb, zum Beispiel 4 oder 4.5."; return null; }
                return v;
            }
            var written = Parse(txtWritten, true);
            var oral    = Parse(txtOral, true);
            var ownBase = Parse(txtOwn, false);
            if (code.Length == 0) problem ??= "Wähle oder tippe ein Fach.";
            if (problem is null && written is null && oral is null && ownBase is null)
                problem = "Trag mindestens eine Prüfungsnote oder einen eigenen Wert ein.";

            if (problem is not null)
            {
                error.Text = problem;
                error.Visibility = Visibility.Visible;
                return;
            }
            CloseDialog();
            _selectedSubject = code;
            AppState.SaveExam(new ExamEntry(code, written, oral, ownBase));   // fires Notify, which rebuilds the page
        }
        save.Click += (_, _) => Save();
        foreach (var box in new UIElement[] { cmbSubject, txtWritten, txtOral, txtOwn })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };

        if (exam is not null)
        {
            var remove = new WpfButton { Content = "Prüfung entfernen", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
            remove.Click += (_, _) => { CloseDialog(); AppState.RemoveExam(exam.Subject); };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            remove.VerticalAlignment = VerticalAlignment.Bottom;
            row.Children.Add(remove);
            var buttons = DialogButtons(save);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            DialogContent.Children.Add(row);
        }
        else
        {
            DialogContent.Children.Add(DialogButtons(save));
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (cmbSubject.Text.Length == 0) cmbSubject.Focus(); else txtWritten.Focus();
        });
    }

    private static string FormatTenth(double value) => value.ToString("0.0##", _deCH);

    private static string FormatExam(double value) => value.ToString("0.0##", _deCH);

    // ── Semester / school year ───────────────────────────────────────────────

    private void BuildPeriodBar()
    {
        PeriodBar.Children.Clear();
        if (AppState.GradePeriods is not { } state || ViewPeriodId is not Guid viewId)
        {
            PeriodBanner.Visibility = Visibility.Collapsed;
            return;
        }
        var period = state.Periods.First(p => p.Id == viewId);

        var delete = new WpfButton
        {
            Content = $"«{period.Name}» löschen", Style = RowButtonStyle(), Padding = new Thickness(10, 6, 10, 6),
            Foreground = new SolidColorBrush(_danger), ToolTip = "Dieses Semester bzw. Jahr mit allen Noten löschen"
        };
        System.Windows.Automation.AutomationProperties.SetName(delete, "Abschnitt löschen");
        delete.Click += (_, _) =>
        {
            int count = AppState.GradesIn(period.Id).Count;
            var text = count == 0
                ? $"«{period.Name}» löschen?"
                : $"«{period.Name}» mit {Count(count, "Note", "Noten")} löschen? Das lässt sich nicht rückgängig machen.";
            if (state.Periods.Count == 1)
                text += "\n\nEs ist der einzige Abschnitt. Danach gibt es keinen mehr, bis du einen neuen startest.";
            if (MessageBox.Show(text, "Abschnitt löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes) return;

            _viewPeriodId    = null;
            _selectedSubject = null;
            AppState.DeleteGradePeriod(period.Id);   // fires Notify, which rebuilds the page
        };
        PeriodBar.Children.Add(delete);

        bool old = viewId != state.CurrentId;
        PeriodBanner.Visibility = old ? Visibility.Visible : Visibility.Collapsed;
        if (old)
            TxtPeriodBanner.Text =
                $"Du siehst «{period.Name}», einen früheren Abschnitt. " +
                $"Noten, die du jetzt einträgst oder änderst, gehören dazu. Laufend ist «{state.Current.Name}».";
    }

    private void ShowNewPeriodDialog()
    {
        var current = AppState.GradePeriods?.Current;
        OpenDialog(current is null ? "Semester oder Schuljahr starten" : "Neues Semester oder Schuljahr",
            current is null
                ? "Deine Noten gehören zu einem Semester oder Schuljahr. Wähle, wie deine Schule die Zeugnisse macht."
                : $"Du startest mit leeren Noten. «{current.Name}» bleibt gespeichert und lässt sich oben jederzeit wieder auswählen.");

        // The first period follows the school: the Kanti gives yearly reports, the others semesters
        var kind = current?.Kind
            ?? (AppState.PromotionRule.Name == PromotionRule.KantiMatur.Name ? PeriodKind.Year : PeriodKind.Semester);
        bool ownName = false;

        var txtName = new WpfTextBox { Height = 36, FontSize = 14, MaxLength = GradePeriod.MaxNameLength };
        System.Windows.Automation.AutomationProperties.SetName(txtName, "Name");

        var btnSemester = new WpfButton { Content = "Semester",  Height = 36, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 0, 16, 0) };
        var btnYear     = new WpfButton { Content = "Schuljahr", Height = 36, Padding = new Thickness(16, 0, 16, 0) };

        void ShowKind()
        {
            btnSemester.Style = (Style)FindResource(kind == PeriodKind.Semester ? "PrimaryButton" : "SecondaryButton");
            btnYear.Style     = (Style)FindResource(kind == PeriodKind.Year     ? "PrimaryButton" : "SecondaryButton");
            // The suggestion follows the kind until someone types a name of their own
            if (!ownName) txtName.Text = GradePeriods.SuggestNext(AppState.GradePeriods, kind, DateTimeOffset.Now);
        }
        btnSemester.Click += (_, _) => { kind = PeriodKind.Semester; ShowKind(); };
        btnYear.Click     += (_, _) => { kind = PeriodKind.Year;     ShowKind(); };
        ShowKind();
        txtName.TextChanged += (_, _) => { if (txtName.IsKeyboardFocusWithin) ownName = true; };

        var kinds = new StackPanel { Orientation = WpfOrientation.Horizontal };
        kinds.Children.Add(btnSemester);
        kinds.Children.Add(btnYear);
        DialogContent.Children.Add(Labeled("Art", kinds));
        DialogContent.Children.Add(new WpfTextBlock
        {
            Text = "Semester: zwei Zeugnisse pro Jahr, wie an der IMS. Schuljahr: ein Jahreszeugnis, wie an der Kanti.",
            Style = (Style)FindResource("NoteText"), Margin = new Thickness(0, -6, 0, 14)
        });
        DialogContent.Children.Add(Labeled("Name", txtName, stretch: true));

        var start = new WpfButton { Content = "Beginnen", Style = (Style)FindResource("PrimaryButton") };
        void Start()
        {
            CloseDialog();
            _viewPeriodId    = null;
            _selectedSubject = null;
            AppState.StartGradePeriod(kind, txtName.Text);   // fires Notify, which rebuilds the page
        }
        start.Click += (_, _) => Start();
        DialogContent.Children.Add(DialogButtons(start));
        txtName.KeyDown += (_, e) => { if (e.Key == Key.Enter) Start(); };
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

        if (rule.Conditions.OfType<MaxDeficitPoints>().FirstOrDefault() is { } deficitRule)
        {
            double deficit = standings.Where(s => s.IsInsufficient).Sum(s => GradeEntry.Sufficient - s.Grade);
            KpiStrip.Children.Add(Kpi("MINUSPUNKTE",
                standings.Count == 0 ? "–" : deficit.ToString("0.#", _deCH),
                $"höchstens {deficitRule.Max.ToString("0.#", _deCH)} erlaubt, 3.5 zählt 0.5",
                deficit > deficitRule.Max ? _danger : null));
        }
        else
        {
            double points = Grading.PlusPoints(standings);
            KpiStrip.Children.Add(Kpi("PLUSPUNKTE",
                standings.Count == 0 ? "–" : SignedPoints(points),
                "+0.5 je halbe Note über 4, −1 darunter",
                standings.Count > 0 && points < 0 ? _danger : null));
        }

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

    private void BtnAddGrade_Click(object sender, RoutedEventArgs e)
    {
        if (_finalMode) ShowExamDialog(_selectedSubject);
        else            ShowGradeDialog(null, _selectedSubject);
    }

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
        if (editing is null && ViewPeriodId is null) { ShowPeriodHint(); return; }

        OpenDialog(editing is null ? "Note eintragen" : "Note bearbeiten",
                   editing is null ? "Eine Note pro Prüfung. Eine halbe Prüfung zählt 0.5, eine Viertelprüfung 0.25." : null);

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
        foreach (var (label, value) in new[] { ("0.25", "0.25"), ("0.5", "0.5"), ("1", "1") })
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
            Date:    DateOnly.FromDateTime(date ?? DateTime.Today),
            PeriodId: editing?.PeriodId ?? ViewPeriodId!.Value));   // fires Notify, which rebuilds the page
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
            MaxDeficitPoints d        => new DraftCondition { Kind = "deficit", Value = Num(d.Max) },
            _                         => throw new InvalidOperationException($"Unknown condition {c.GetType().Name}")
        }).ToList();

        static string Num(double d) => d.ToString("0.##", _deCH);
    }

    private void ShowRuleDialog()
    {
        OpenDialog("Promotionsregel", "Wähle das System deiner Schule und pass es an. Alle Bedingungen müssen erfüllt sein.");

        var schools = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
        schools.Children.Add(TemplateCard("Kanti Aarau · Matur",
            "Pluspunkte ≥ 0 (Minus doppelt), höchstens 4 unter 4", PromotionRule.KantiMatur, "KANTI-STANDARD"));
        schools.Children.Add(TemplateCard("IMS Aarau · BM",
            "Gesamtnote ≥ 4.0, höchstens 2 ungenügend, höchstens 2 Minuspunkte, INF ≥ 4", PromotionRule.ImsBm, "IMS-STANDARD"));
        DialogContent.Children.Add(Labeled("Vorlage deiner Schule", schools, stretch: true));

        var generic = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 16) };
        generic.Children.Add(TemplateCard("Pluspunkte", "Pluspunkte ≥ 0, höchstens 2 ungenügend", PromotionRule.PlusPointsSystem));
        generic.Children.Add(TemplateCard("Gesamtschnitt", "Schnitt ≥ 4, höchstens 2 ungenügend", PromotionRule.AverageSystem));
        generic.Children.Add(TemplateCard("Eigene", "Leer beginnen und selbst bauen", new PromotionRule("Eigene Regel", [])));
        DialogContent.Children.Add(Labeled("Allgemeine Vorlagen", generic, stretch: true));

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
        adders.Children.Add(AddConditionButton("+ Minuspunkte unter 4", new DraftCondition { Kind = "deficit", Value = "2" }));
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

    private UIElement TemplateCard(string title, string text, PromotionRule template, string? badge = null)
    {
        var panel = new StackPanel();
        if (badge is not null)
            panel.Children.Add(new WpfBorder
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = WpfHA.Left,
                Background = new SolidColorBrush(Color.FromArgb(40, _accent.R, _accent.G, _accent.B)),
                Child = new WpfTextBlock
                {
                    Text = badge, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(_accent)
                }
            });
        panel.Children.Add(new WpfTextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new WpfTextBlock { Text = text, FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var btn = new WpfButton
        {
            Content = panel, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 8),
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
            case "deficit":
                row.Children.Add(Text("Höchstens"));
                row.Children.Add(Box(d.Value, 50, v => d.Value = v));
                row.Children.Add(Text("Minuspunkte unter 4"));
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
                case "deficit" when value < 0:
                    problem = "Minuspunkte können nicht negativ sein.";
                    return false;
                case "deficit":
                    conditions.Add(new MaxDeficitPoints(value));
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

    public string TourKey => "grades";

    /// <summary>The tour waits until a rule is chosen; before that the page shows only the choice.</summary>
    public bool TourReady => AppState.HasChosenRule;

    public IReadOnlyList<TourStep> TourSteps() =>
    [
        new(() => BtnAddGrade, "Note eintragen",
            "Hier trägst du eine Note ein: Fach, Note und Gewicht. Eine halbe Prüfung zählt 0.5, eine Viertelprüfung 0.25."),
        new(() => PeriodTabs, "Semester und Schuljahre",
            "Jedes Semester oder Schuljahr ist ein Tab. Mit «+ Semester / Jahr» beginnst du frisch, frühere bleiben hier."),
        new(() => FinalTab, "Abschluss",
            "Hier fügst du deinen Abschluss hinzu: Matur an der Kanti Aarau oder BM an der IMS Aarau. Semestria rechnet die Noten aus Zeugnissen und Prüfungen."),
        new(() => KpiStrip, "Dein Überblick",
            "Promotion, Gesamtschnitt, Pluspunkte und ungenügende Fächer auf einen Blick. Rot heisst: hier aufpassen."),
        new(() => BtnRule, "Die Regel deiner Schule",
            "Hier wählst du, wie deine Schule rechnet. Für die Kanti Aarau und die IMS Aarau gibt es fertige Vorlagen, alles andere lässt sich selbst zusammenstellen."),
        new(() => SubjectCard, "Deine Fächer",
            "Jede Zeile ist ein Fach: alle Noten, der genaue Schnitt und die auf halbe Noten gerundete Fachnote. Klick auf ein Fach für Details."),
        new(() => _calculatorCard, "Zielrechner",
            "Wähl eine Ziel-Fachnote und das Gewicht der nächsten Prüfung. Semestria sagt dir, welche Note du dafür brauchst."),
        new(() => BtnHelp, "Jederzeit wieder",
            "Mit dem Fragezeichen hier oder unter «Hilfe» unten links startest du diese Anleitung noch einmal."),
    ];

    private void BtnHelp_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
        if (_finalMode) { _finalMode = false; Refresh(); }
        MainWindow.Current?.StartTour(this);
    }

    /// <summary>Escape closes an open dialog.</summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DialogLayer.Visibility == Visibility.Visible)
        {
            CloseDialog();
            e.Handled = true;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Small helpers
    // ══════════════════════════════════════════════════════════════════════

    private IEnumerable<GradeEntry> GradesOf(string subject)
        => _viewGrades
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
        var text = FormatGrade(g.Value) + WeightMark(g.Weight);
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

    /// <summary>Short weight marker for a chip: nothing for 1, ½ and ¼ for the usual halves and quarters.</summary>
    private static string WeightMark(double weight) => weight switch
    {
        1    => "",
        0.5  => " ½",
        0.25 => " ¼",
        _    => $" ×{weight.ToString("0.##", _deCH)}"
    };

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
