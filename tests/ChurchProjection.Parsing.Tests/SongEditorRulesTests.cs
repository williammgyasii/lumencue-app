using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SongEditorRulesTests
{
    [Theory]
    [InlineData("amazing grace", "Amazing grace")]
    [InlineData("AMAZING GRACE", "Amazing grace")]
    [InlineData("  10,000 Reasons  ", "10,000 reasons")]
    [InlineData("", "")]
    public void ToSentenceCase_CapitalizesTheFirstLetterAndLowersTheRest(string input, string expected)
        => Assert.Equal(expected, SongTitle.ToSentenceCase(input));

    [Fact]
    public void CanSave_RequiresTitleAndArtist()
    {
        Assert.False(SongEditorRules.CanSave(title: "Amazing grace", artist: ""));
        Assert.False(SongEditorRules.CanSave(title: "", artist: "CEYC"));
        Assert.True(SongEditorRules.CanSave(title: "Amazing grace", artist: "CEYC"));
    }

    [Fact]
    public void SameBreakdown_IsTrueOnlyWhenTypeAndTextMatchInOrder()
    {
        var left = new List<(string Type, string Text)> { ("verse", "a"), ("chorus", "b") };
        Assert.True(SongEditorRules.SameBreakdown(left, [("verse", "a"), ("chorus", "b")]));
        Assert.False(SongEditorRules.SameBreakdown(left, [("verse", "a"), ("chorus", "changed")]));
    }

    [Theory]
    [InlineData(0, "Auto")]
    [InlineData(4, "4")]
    [InlineData(-1, "Auto")]
    public void LinesChoice_RoundTripsAutoAndCounts(int lines, string choice)
    {
        Assert.Equal(choice, SongLinesPerSlide.ToChoice(lines));
        Assert.Equal(lines < 0 ? 0 : lines, SongLinesPerSlide.FromChoice(choice));
    }

    [Fact]
    public void SplitPages_TwoLinesMakesTwoCards_SixLinesKeepsOne()
    {
        const string verse = """
            Marvelous grace of our loving Lord,
            grace that exceeds our sin and our guilt,
            yonder on Calvary's mount out-poured,
            there where the blood of the Lamb was spilt.
            """;

        Assert.Equal(2, SongLinesPerSlide.SplitPages(verse, 2).Count);
        Assert.Equal(1, SongLinesPerSlide.SplitPages(verse, 6).Count);
        Assert.Equal(1, SongLinesPerSlide.SplitPages(verse, 0).Count);
    }
}
