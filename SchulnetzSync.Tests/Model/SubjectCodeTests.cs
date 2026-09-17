using SchulnetzSync.Core.Model;
using Xunit;

namespace SchulnetzSync.Tests.Model;

public class SubjectCodeTests
{
    [Theory]
    [InlineData("TEU_I26A",                             "TEU")]
    [InlineData("9:30 TEU_I26A",                        "TEU")]
    [InlineData("13:55 ILA_I26A",                       "ILA")]
    [InlineData("DEU_I26A_SmiJa",                       "DEU")]
    [InlineData("FRA_I26A_DuaLi Examen de vocabulaire", "FRA")]
    [InlineData("TEU_I26A Prüfung 1.1_GEO_TEU",         "TEU")]
    [InlineData("frw_i26a",                             "FRW")]
    public void TakesPartBeforeFirstUnderscore(string summary, string expected)
        => Assert.Equal(expected, SubjectCode.FromSummary(summary));

    [Fact]
    public void LessonAndExamOfSameSubject_ShareCode()
        => Assert.Equal(SubjectCode.FromSummary("9:30 TEU_I26A"),
                        SubjectCode.FromSummary("TEU_I26A Prüfung 1.1_GEO_TEU"));

    [Theory]
    [InlineData("spm 1.1 IMS", "SPM 1.")]
    [InlineData("Sport",       "SPORT")]
    public void WithoutUnderscore_TakesFirstSixCharacters(string summary, string expected)
        => Assert.Equal(expected, SubjectCode.FromSummary(summary));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptySummary_GivesEmptyCode(string? summary)
        => Assert.Equal("", SubjectCode.FromSummary(summary));
}
