using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Brushes        = System.Windows.Media.Brushes;
using Size           = System.Windows.Size;
using WpfBorder      = System.Windows.Controls.Border;
using WpfButton      = System.Windows.Controls.Button;
using WpfHA          = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPath        = System.Windows.Shapes.Path;
using WpfTextBlock   = System.Windows.Controls.TextBlock;

namespace SchulnetzSync.UI.Controls;

/// <summary>One stop of a guided tour: what to point at and what to say.</summary>
/// <param name="Target">The element, asked anew each time, since pages rebuild themselves.</param>
public sealed record TourStep(Func<FrameworkElement?> Target, string Title, string Text);

/// <summary>A page with a short guided tour of its own.</summary>
public interface ITourPage
{
    /// <summary>Remembers per profile which tours were seen, e.g. "tasks".</summary>
    string TourKey { get; }

    /// <summary>False while the page is not ready for its tour (the grade page asks for a rule first).</summary>
    bool TourReady => true;

    IReadOnlyList<TourStep> TourSteps();
}

/// <summary>
/// The guided tours of all pages, drawn on one canvas above the whole main window: the
/// window is dimmed, a hole is cut around the element of the current step, and a bubble
/// next to it explains it, with back, next and skip. Also shows single hints, like «start a
/// semester first», with an action button.
/// </summary>
public sealed class TourOverlay
{
    private static readonly Color Accent = Color.FromRgb(0x5C, 0x6E, 0xF7);

    private readonly Canvas _layer;
    private IReadOnlyList<TourStep> _steps = [];
    private int _index = -1;
    private Action? _finished;

    // Set while a single hint instead of a tour is shown
    private string? _hintAction;
    private Action? _onHintAction;

    public TourOverlay(Canvas layer)
    {
        _layer = layer;
        _layer.Background = Brushes.Transparent;   // catches clicks, so the page stays still
        _layer.Visibility = Visibility.Collapsed;
        _layer.SizeChanged += (_, _) => Position();

        // Any scrolling underneath moves the target
        if (_layer.Parent is UIElement root)
        {
            root.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => Position()), true);
            root.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    public bool IsActive => _index >= 0;

    /// <summary>Runs a tour; <paramref name="finished"/> is called when it ends, skipped or not.</summary>
    public void Start(IReadOnlyList<TourStep> steps, Action? finished)
    {
        if (steps.Count == 0) { finished?.Invoke(); return; }
        _steps       = steps;
        _finished    = finished;
        _hintAction  = null;
        _onHintAction = null;
        _index = 0;
        _layer.Visibility = Visibility.Visible;
        ShowStep();
    }

    /// <summary>A single bubble with «Später» and an action, e.g. «Jetzt starten».</summary>
    public void ShowHint(TourStep step, string actionLabel, Action action)
    {
        _steps        = [step];
        _finished     = null;
        _hintAction   = actionLabel;
        _onHintAction = action;
        _index = 0;
        _layer.Visibility = Visibility.Visible;
        ShowStep();
    }

    public void Close()
    {
        if (_index < 0) return;
        _index = -1;
        _layer.Visibility = Visibility.Collapsed;
        _layer.Children.Clear();
        var finished = _finished;
        _finished = null;
        finished?.Invoke();
    }

    private void Go(int index)
    {
        if (index < 0) return;
        if (index >= _steps.Count) { Close(); return; }
        _index = index;
        ShowStep();
    }

    private void ShowStep()
    {
        // Scroll the element into view first, then measure once the layout has settled
        _steps[_index].Target()?.BringIntoView();
        _layer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Position);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_index < 0) return;
        switch (e.Key)
        {
            case Key.Escape:                                   Close();       e.Handled = true; break;
            case Key.Right when _hintAction is null:          Go(_index + 1); e.Handled = true; break;
            case Key.Left  when _hintAction is null && _index > 0: Go(_index - 1); e.Handled = true; break;
        }
    }

    /// <summary>Dims the window, cuts a hole around the current element and places the bubble.</summary>
    private void Position()
    {
        if (_index < 0 || _layer.Visibility != Visibility.Visible) return;

        var step   = _steps[_index];
        var target = step.Target();
        double w = _layer.ActualWidth, h = _layer.ActualHeight;
        if (w == 0 || h == 0) return;

        _layer.Children.Clear();

        Rect hole = Rect.Empty;
        if (target is { IsVisible: true } && PresentationSource.FromVisual(target) is not null
            && target.ActualWidth > 0 && target.ActualHeight > 0)
        {
            // TransformToVisual, not ToAncestor: the layer is a sibling of the content
            hole = target.TransformToVisual(_layer)
                         .TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            hole.Inflate(8, 8);
            hole.Intersect(new Rect(0, 0, w, h));
        }

        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry(new Rect(0, 0, w, h)));
        if (!hole.IsEmpty) dim.Children.Add(new RectangleGeometry(hole, 12, 12));
        _layer.Children.Add(new WpfPath { Data = dim, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)) });

        if (!hole.IsEmpty)
        {
            var ring = new WpfBorder
            {
                Width = hole.Width, Height = hole.Height, CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Accent), BorderThickness = new Thickness(2), IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, hole.X);
            Canvas.SetTop(ring, hole.Y);
            _layer.Children.Add(ring);
        }

        var bubble = Bubble(step);
        bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = bubble.DesiredSize;

        // Below the element when there is room, else above, else right of it, else centred
        double x, y;
        if (hole.IsEmpty)
        {
            x = (w - size.Width) / 2;
            y = (h - size.Height) / 2;
        }
        else if (hole.Bottom + 12 + size.Height <= h - 16)
        {
            x = Math.Clamp(hole.X, 16, Math.Max(16, w - size.Width - 16));
            y = hole.Bottom + 12;
        }
        else if (hole.Top - 12 - size.Height >= 16)
        {
            x = Math.Clamp(hole.X, 16, Math.Max(16, w - size.Width - 16));
            y = hole.Top - 12 - size.Height;
        }
        else if (hole.Right + 12 + size.Width <= w - 16)
        {
            x = hole.Right + 12;
            y = Math.Clamp(hole.Y, 16, Math.Max(16, h - size.Height - 16));
        }
        else
        {
            x = (w - size.Width) / 2;
            y = Math.Max(16, (h - size.Height) / 2);
        }
        Canvas.SetLeft(bubble, x);
        Canvas.SetTop(bubble, y);
        _layer.Children.Add(bubble);
    }

    private FrameworkElement Bubble(TourStep step)
    {
        bool hint = _hintAction is not null;
        var panel = new StackPanel();
        panel.Children.Add(new WpfTextBlock
        {
            Text = hint ? "HINWEIS" : $"{_index + 1} VON {_steps.Count}",
            FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Accent)
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = step.Title, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = step.Text, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Opacity = 0.9
        });

        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        WpfButton primary;

        if (hint)
        {
            var later = Button("Später", primaryStyle: false);
            later.HorizontalAlignment = WpfHA.Left;
            later.Click += (_, _) => Close();
            buttons.Children.Add(later);

            primary = Button(_hintAction!, primaryStyle: true);
            var action = _onHintAction;
            primary.Click += (_, _) => { Close(); action?.Invoke(); };
            Grid.SetColumn(primary, 1);
            buttons.Children.Add(primary);
        }
        else
        {
            bool last = _index == _steps.Count - 1;
            if (!last)
            {
                var skip = Button("Überspringen", primaryStyle: false);
                skip.HorizontalAlignment = WpfHA.Left;
                skip.Click += (_, _) => Close();
                buttons.Children.Add(skip);
            }

            var nav = new StackPanel { Orientation = WpfOrientation.Horizontal };
            if (_index > 0)
            {
                var back = Button("Zurück", primaryStyle: false);
                back.Margin = new Thickness(0, 0, 8, 0);
                back.Click += (_, _) => Go(_index - 1);
                nav.Children.Add(back);
            }
            primary = Button(last ? "Fertig" : "Weiter", primaryStyle: true);
            primary.Click += (_, _) => Go(_index + 1);
            nav.Children.Add(primary);
            Grid.SetColumn(nav, 1);
            buttons.Children.Add(nav);
        }
        panel.Children.Add(buttons);
        primary.Loaded += (_, _) => primary.Focus();

        return new WpfBorder
        {
            Width = 340, Padding = new Thickness(20, 16, 20, 16), CornerRadius = new CornerRadius(12),
            Background = (Brush)_layer.FindResource("SystemControlBackgroundChromeMediumBrush"),
            BorderBrush = new SolidColorBrush(Accent), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35 },
            Child = panel
        };
    }

    private WpfButton Button(string text, bool primaryStyle) => new()
    {
        Content = text,
        Style   = (Style)_layer.FindResource(primaryStyle ? "PrimaryButton" : "SecondaryButton")
    };
}
