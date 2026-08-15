using ChurchProjection.Core.Models.Theme;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ThemeApplyPolicyTests
{
    [Fact]
    public void SameTheme_KeepsTheLook_SoTheNextVerseIsJustText()
    {
        var theme = new Theme { Name = "House" };

        Assert.False(ThemeApplyPolicy.NeedsFullApply(theme, theme, leavingBlank: false));
    }

    [Fact]
    public void DifferentTheme_Rebuilds()
    {
        var a = new Theme { Name = "A" };
        var b = new Theme { Name = "B" };

        Assert.True(ThemeApplyPolicy.NeedsFullApply(a, b, leavingBlank: false));
    }

    [Fact]
    public void LeavingBlank_RebuildsEvenOnTheSameTheme()
    {
        var theme = new Theme { Name = "House" };

        Assert.True(ThemeApplyPolicy.NeedsFullApply(theme, theme, leavingBlank: true));
    }

    [Fact]
    public void FirstApply_Rebuilds()
    {
        Assert.True(ThemeApplyPolicy.NeedsFullApply(null, new Theme { Name = "House" }, leavingBlank: false));
    }
}
