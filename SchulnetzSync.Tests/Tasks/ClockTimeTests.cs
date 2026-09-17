using SchulnetzSync.Core.Tasks;
using Xunit;

namespace SchulnetzSync.Tests.Tasks;

public class ClockTimeTests
{
    [Theory]
    [InlineData("08:30", 8, 30)]
    [InlineData("8:30",  8, 30)]
    [InlineData("8.30",  8, 30)]   // Schweizer Schreibweise
    [InlineData("08.30", 8, 30)]
    [InlineData("8h30",  8, 30)]
    [InlineData("830",   8, 30)]
    [InlineData("0830",  8, 30)]
    [InlineData("1415", 14, 15)]
    [InlineData("8",     8,  0)]
    [InlineData("14",   14,  0)]
    [InlineData("8h",    8,  0)]
    [InlineData(" 9:05 ", 9, 5)]
    [InlineData("0:00",  0,  0)]
    [InlineData("23:59", 23, 59)]
    public void Liest_uebliche_Schreibweisen(string input, int hours, int minutes)
    {
        Assert.True(ClockTime.TryParse(input, out var time));
        Assert.Equal(new TimeSpan(hours, minutes, 0), time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("24:00")]
    [InlineData("8:60")]
    [InlineData("8:5")]      // 8:05 oder 8:50? nicht raten
    [InlineData("12345")]
    [InlineData("8:30:00")]
    [InlineData("-8:30")]
    [InlineData("abc")]
    [InlineData("8 30")]
    public void Verwirft_Unlesbares(string? input)
    {
        Assert.False(ClockTime.TryParse(input, out _));
    }
}
