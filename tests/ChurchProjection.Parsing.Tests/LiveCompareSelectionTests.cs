using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class LiveCompareSelectionTests
{
    [Fact]
    public void Parse_KeepsAtMostTwo()
    {
        var chosen = LiveCompareSelection.Parse("MSG, AMP, NIV");

        Assert.Equal(["MSG", "AMP"], chosen);
    }

    [Fact]
    public void ForDisplay_SkipsTheTranslationAlreadyLive()
    {
        var shown = LiveCompareSelection.ForDisplay(["MSG", "AMP"], liveTranslation: "MSG");

        Assert.Equal(["AMP"], shown);
    }

    [Fact]
    public void ForDisplay_DoesNotFillFromAvailable()
    {
        var shown = LiveCompareSelection.ForDisplay(
            ["AMP"],
            liveTranslation: "MSG",
            available: ["KJV", "MSG", "AMP", "NIV"]);

        Assert.Equal(["AMP"], shown);
    }

    [Fact]
    public void ForDisplay_OnePickShowsOneCard()
    {
        var shown = LiveCompareSelection.ForDisplay(["NIV"], liveTranslation: "BSB");

        Assert.Equal(["NIV"], shown);
    }

    [Fact]
    public void ForDisplay_TwoPicksShowTwoCards()
    {
        var shown = LiveCompareSelection.ForDisplay(["NIV", "KJV"], liveTranslation: "BSB");

        Assert.Equal(["NIV", "KJV"], shown);
    }

    [Fact]
    public void ForDisplay_ShowsBothWhenNeitherIsLive()
    {
        var shown = LiveCompareSelection.ForDisplay(["MSG", "AMP"], liveTranslation: "KJV");

        Assert.Equal(["MSG", "AMP"], shown);
    }

    [Fact]
    public void Sanitize_DropsCodesNotInThePicker()
    {
        var kept = LiveCompareSelection.Sanitize(["MSG", "NIV", "AMP"], ["BSB", "KJV", "NIV"]);

        Assert.Equal(["NIV"], kept);
    }

    [Fact]
    public void Toggle_AddsUntilTheTwoSlotCap()
    {
        var chosen = new List<string> { "MSG" };

        Assert.True(LiveCompareSelection.Toggle(chosen, "AMP"));
        Assert.False(LiveCompareSelection.Toggle(chosen, "NIV"));
        Assert.Equal(["MSG", "AMP"], chosen);
    }

    [Fact]
    public void Toggle_UnchecksAnExistingCode()
    {
        var chosen = new List<string> { "MSG", "AMP" };

        Assert.True(LiveCompareSelection.Toggle(chosen, "MSG"));
        Assert.Equal(["AMP"], chosen);
    }
}
