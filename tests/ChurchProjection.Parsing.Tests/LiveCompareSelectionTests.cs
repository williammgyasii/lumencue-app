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
    public void ForDisplay_FillsTheEmptySlotWhenACardGoesLive()
    {
        var shown = LiveCompareSelection.ForDisplay(
            ["MSG", "AMP"],
            liveTranslation: "MSG",
            available: ["KJV", "MSG", "AMP", "NIV"]);

        Assert.Equal(["AMP", "KJV"], shown);
    }

    [Fact]
    public void ForDisplay_ShowsBothWhenNeitherIsLive()
    {
        var shown = LiveCompareSelection.ForDisplay(["MSG", "AMP"], liveTranslation: "KJV");

        Assert.Equal(["MSG", "AMP"], shown);
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
