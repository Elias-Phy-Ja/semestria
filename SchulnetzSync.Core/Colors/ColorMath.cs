using System.Globalization;

namespace SchulnetzSync.Core.Colors;

/// <summary>An sRGB colour, 8 bits per channel.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Formats as <c>#RRGGBB</c>, the form the colour files store.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Parses <c>#RRGGBB</c>, <c>RRGGBB</c>, <c>#RGB</c> or <c>RGB</c>.</summary>
    /// <returns>False for anything else, alpha values included.</returns>
    public static bool TryParseHex(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];

        // Short CSS form #RGB: double every digit.
        if (s.Length == 3)
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);

        if (s.Length != 6) return false;

        if (!byte.TryParse(s[0..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(s[2..4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(s[4..6], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
            return false;

        color = new RgbColor(r, g, b);
        return true;
    }
}
/// <summary>
/// A colour as hue, saturation and value — the model behind the colour mixer.
///
/// The mixer works in HSV rather than RGB because that is how people actually mix:
/// one slider for the hue, one surface for how strong and how bright it should be.
/// </summary>
/// <param name="H">Hue in degrees, 0 up to (but not including) 360.</param>
/// <param name="S">Saturation, 0 is grey, 1 is full colour.</param>
/// <param name="V">Value, 0 is black, 1 is full brightness.</param>
public readonly record struct HsvColor(double H, double S, double V)
{
    public RgbColor ToRgb()
    {
        double h = ((H % 360) + 360) % 360;   // folds negative values and 360 back into range
        double s = Math.Clamp(S, 0, 1);
        double v = Math.Clamp(V, 0, 1);

        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;

        // Each 60° sector puts the three channels in a different order.
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new RgbColor(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public static HsvColor FromRgb(RgbColor color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        // Grey has no hue at all, so leave it at 0 instead of dividing by zero.
        double h = 0;
        if (delta > 0)
        {
            if (max == r)      h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else               h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max == 0 ? 0 : delta / max;
        return new HsvColor(h, s, max);
    }

    private static byte ToByte(double channel)
        => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255, MidpointRounding.AwayFromZero);
}
