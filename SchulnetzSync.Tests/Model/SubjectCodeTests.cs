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

    [Theory]
    [InlineData("DEU_I26A_SmiJa",                            "DEU", "")]
    [InlineData("9:30 TEU_I26A",                             "TEU", "")]
    [InlineData("INF_I26A M431",                             "INF", "M431")]
    [InlineData("FRA_I26A_DuaLi Examen de grammaire Lecons", "FRA", "Examen de grammaire Lecons")]
    [InlineData("WIR_I26A_KliLi Pruefung Swir 1.3",          "WIR", "Pruefung Swir 1.3")]
    public void Split_SeparatesCodeFromRest(string summary, string code, string rest)
    {
        var (actualCode, actualRest) = SubjectCode.Split(summary);

        Assert.Equal(code, actualCode);
        Assert.Equal(rest, actualRest);
    }

    [Theory]
    [InlineData("Begruessung und Einfuehrung 1. Klasse")]
    [InlineData("spm 1.1 IMS Speerwurf")]
    [InlineData("TecDay")]
    public void Split_WithoutCode_KeepsWholeTitle(string summary)
    {
        var (code, rest) = SubjectCode.Split(summary);

        Assert.Equal("", code);
        Assert.Equal(summary, rest);
    }

    [Fact]
    public void Split_EmptySummary_GivesTwoEmptyParts()
        => Assert.Equal(("", ""), SubjectCode.Split(null));
}
