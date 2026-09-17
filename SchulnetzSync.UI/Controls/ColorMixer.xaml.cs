using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SchulnetzSync.Core.Colors;
using Canvas      = System.Windows.Controls.Canvas;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point       = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace SchulnetzSync.UI.Controls;

/// <summary>
/// Free colour picker: a saturation/brightness field, a hue slider and a hex box.
///
/// Rechnet intern in HSV (siehe <see cref="HsvColor"/>). Der Farbton wird
/// getrennt gehalten, damit er beim Ziehen durch Grau oder Schwarz nicht
/// verloren geht — aus reinem RGB liesse er sich dort nicht zurückgewinnen.
/// </summary>
public partial class ColorMixer : UserControl
{
    private HsvColor _hsv = new(221, 0.84, 0.92);   // Standardblau der App

    /// <summary>Verhindert, dass das Hex-Feld sich selbst neu schreibt, während man tippt.</summary>
    private bool _typingHex;

    /// <summary>Raised whenever the colour changes, by dragging or typing.</summary>
    public event Action<RgbColor>? ColorChanged;

    public ColorMixer()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisuals(updateHex: true);
    }

    /// <summary>The colour currently mixed.</summary>
    public RgbColor SelectedColor
    {
        get => _hsv.ToRgb();
        set
        {
            _hsv = HsvColor.FromRgb(value);
            UpdateVisuals(updateHex: true);
        }
    }

    // ── Sättigung / Helligkeit ───────────────────────────────────────────────

    private void SvArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.CaptureMouse();
        SetSvFromPoint(e.GetPosition(SvArea));
    }

    private void SvArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured) SetSvFromPoint(e.GetPosition(SvArea));
    }

    private void SetSvFromPoint(Point p)
    {
        double s = Math.Clamp(p.X / Math.Max(1, SvArea.ActualWidth), 0, 1);
        double v = 1 - Math.Clamp(p.Y / Math.Max(1, SvArea.ActualHeight), 0, 1);
        _hsv = _hsv with { S = s, V = v };
        UpdateVisuals(updateHex: true);
    }

    // ── Farbton ──────────────────────────────────────────────────────────────

    private void HueArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HueArea.CaptureMouse();
        SetHueFromPoint(e.GetPosition(HueArea));
    }

    private void HueArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (HueArea.IsMouseCaptured) SetHueFromPoint(e.GetPosition(HueArea));
    }

    private void SetHueFromPoint(Point p)
    {
        // Knapp unter 360, damit ganz rechts nicht wieder zu 0° umspringt
        double h = Math.Clamp(p.X / Math.Max(1, HueArea.ActualWidth), 0, 1) * 359.9;
        _hsv = _hsv with { H = h };
        UpdateVisuals(updateHex: true);
    }

    private void Area_MouseUp(object sender, MouseButtonEventArgs e)
        => ((UIElement)sender).ReleaseMouseCapture();

    private void Area_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateVisuals(updateHex: false, notify: false);

    // ── Hex-Eingabe ──────────────────────────────────────────────────────────

    private void TxtHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_typingHex) return;
        if (!RgbColor.TryParseHex(TxtHex.Text, out var rgb)) return;

        var parsed = HsvColor.FromRgb(rgb);

        // Grau hat keinen eigenen Farbton — den bisherigen behalten
        _hsv = parsed.S == 0 ? parsed with { H = _hsv.H } : parsed;
        UpdateVisuals(updateHex: false);
    }

    // ── Darstellung ──────────────────────────────────────────────────────────

    private void UpdateVisuals(bool updateHex, bool notify = true)
    {
        if (!IsInitialized) return;

        var rgb = _hsv.ToRgb();
        var pure = new HsvColor(_hsv.H, 1, 1).ToRgb();

        SvHue.Background   = new SolidColorBrush(Color.FromRgb(pure.R, pure.G, pure.B));
        Preview.Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));

        Canvas.SetLeft(SvThumb, _hsv.S * SvArea.ActualWidth - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _hsv.V) * SvArea.ActualHeight - SvThumb.Height / 2);
        Canvas.SetLeft(HueThumb, _hsv.H / 360 * HueArea.ActualWidth - HueThumb.Width / 2);

        if (updateHex)
        {
            _typingHex = true;
            TxtHex.Text = rgb.ToHex();
            _typingHex = false;
        }

        if (notify) ColorChanged?.Invoke(rgb);
    }
}
