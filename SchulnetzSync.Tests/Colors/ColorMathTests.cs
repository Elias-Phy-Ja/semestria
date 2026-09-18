using SchulnetzSync.Core.Colors;
using Xunit;

namespace SchulnetzSync.Tests.Colors;

/// <summary>Colour conversions the mixer relies on, including the awkward corners.</summary>
public class ColorMathTests
{
    [Theory]
    [InlineData(0,   1, 1, "#FF0000")]
    [InlineData(120, 1, 1, "#00FF00")]
    [InlineData(240, 1, 1, "#0000FF")]
    [InlineData(60,  1, 1, "#FFFF00")]
    [InlineData(0,   0, 1, "#FFFFFF")]
    [InlineData(0,   0, 0, "#000000")]
    [InlineData(200, 0, 0.5, "#808080")]   // without saturation the hue is irrelevant
    [InlineData(360, 1, 1, "#FF0000")]     // 360° wraps back to red
    [InlineData(-120, 1, 1, "#0000FF")]    // negative angles run backwards
    public void Hsv_nach_Rgb(double h, double s, double v, string expected)
    {
        Assert.Equal(expected, new HsvColor(h, s, v).ToRgb().ToHex());
    }

    [Theory]
    [InlineData("#2563EB")]   // the default blue of the app
    [InlineData("#DC2626")]
    [InlineData("#D97706")]
    [InlineData("#16A34A")]
    [InlineData("#7C3AED")]
    [InlineData("#1E293B")]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    public void Rgb_ueber_Hsv_bleibt_erhalten(string hex)
    {
        Assert.True(RgbColor.TryParseHex(hex, out var rgb));

        var roundTrip = HsvColor.FromRgb(rgb).ToRgb();

        Assert.Equal(hex, roundTrip.ToHex());
    }

    [Theory]
    [InlineData("#2563EB", 0x25, 0x63, 0xEB)]
    [InlineData("2563eb",  0x25, 0x63, 0xEB)]
    [InlineData(" #fff ",  0xFF, 0xFF, 0xFF)]
    [InlineData("#0a0",    0x00, 0xAA, 0x00)]
    public void Liest_Hex_Schreibweisen(string input, byte r, byte g, byte b)
    {
        Assert.True(RgbColor.TryParseHex(input, out var color));
        Assert.Equal(new RgbColor(r, g, b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData("#FF2563EB")]   // an alpha channel is not accepted
    [InlineData("blau")]
    public void Verwirft_ungueltiges_Hex(string? input)
    {
        Assert.False(RgbColor.TryParseHex(input, out _));
    }
}
