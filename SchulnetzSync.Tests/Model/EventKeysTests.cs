using SchulnetzSync.Core.Model;

namespace SchulnetzSync.Tests.Model;

/// <summary>
/// Keys for hand-made entries. Editing an entry has to find it again by its key, so the
/// id must survive the round trip through <see cref="EventKeys.ForManual"/>.
/// </summary>
public class EventKeysTests
{
    [Fact]
    public void ManualKey_RoundTripsToTheSameId()
    {
        var id = Guid.NewGuid();

        Assert.True(EventKeys.TryGetManualId(EventKeys.ForManual(id), out var back));
        Assert.Equal(id, back);
    }

    [Theory]
    [InlineData("P_65100")]
    [InlineData("T_7409")]
    [InlineData("20260810et48167et13:05et13:50et014")]
    [InlineData("MANUAL_not-a-guid")]
    [InlineData("")]
    [InlineData(null)]
    public void NonManualOrBrokenKey_GivesNoId(string? key)
    {
        Assert.False(EventKeys.TryGetManualId(key, out var id));
        Assert.Equal(Guid.Empty, id);
    }
}
