using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Theme;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class LowerThirdFitTests
{
    [Fact]
    public void Slide_regions_clip_to_their_box()
    {
        Assert.True(OperatorWorkspaceChrome.SlideRegionsClipToBounds);
    }

    [Fact]
    public void Short_verse_fits_the_lower_third_box()
    {
        var theme = LowerThird();
        var box = theme.UsablePaginationBox(theme.ResolvePaginationRegion(SlideType.Scripture));

        Assert.True(ThemeTextBox.Fits(
            "For God so loved the world.",
            box.Width, box.Height, theme.BodyFontSize, theme.LineHeightMultiplier));
    }

    [Fact]
    public void Tall_body_does_not_fit_the_lower_third_box()
    {
        var theme = LowerThird();
        var box = theme.UsablePaginationBox(theme.ResolvePaginationRegion(SlideType.Scripture));
        var body = string.Join(' ', Enumerable.Repeat("overflow", 400));

        Assert.False(ThemeTextBox.Fits(
            body, box.Width, box.Height, theme.BodyFontSize, theme.LineHeightMultiplier));
        Assert.True(box.Height < Theme.CanvasHeight / 2);
    }

    private static Theme LowerThird() => new()
    {
        Name = "Lower Third",
        Layout = ThemeLayout.LowerThird,
        BodyFontSize = 54,
        Bold = true,
    };
}
