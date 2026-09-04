using System.Linq;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class OperatorWorkspaceChromeTests
{
    [Fact]
    public void Top_tabs_have_material_icons_and_labels()
    {
        Assert.Equal(5, OperatorWorkspaceChrome.NavItems.Count);
        Assert.Equal(
            new[] { "Bible", "Songs", "Media", "Notes", "Themes" },
            OperatorWorkspaceChrome.NavItems.Select(i => i.Label).ToArray());
        Assert.All(OperatorWorkspaceChrome.NavItems, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.IconKind));
            Assert.True(item.IconKind.All(char.IsAsciiLetter), item.IconKind);
        });
        Assert.False(OperatorWorkspaceChrome.NavItems.Single(i => i.Label == "Themes").IsWorkspaceMode);
    }

    [Fact]
    public void Active_tab_is_a_full_box_and_inactive_tabs_are_grayed()
    {
        Assert.True(OperatorWorkspaceChrome.HighlightEntireActiveTab);
        Assert.Equal(1, OperatorWorkspaceChrome.ActiveOpacity);
        Assert.True(OperatorWorkspaceChrome.InactiveOpacity < 1);
        Assert.True(OperatorWorkspaceChrome.InactiveOpacity > 0);
    }

    [Fact]
    public void Compare_is_a_separate_left_view_from_bookmarks()
    {
        Assert.True(OperatorWorkspaceChrome.CompareIsSeparateView);
        Assert.False(OperatorWorkspaceChrome.CompareUnderBookmarks);
        Assert.True(OperatorWorkspaceChrome.CompareClipsToPane);
        Assert.True(OperatorWorkspaceChrome.CompareCardInset * 2 + 2 < 248);
        Assert.True(OperatorWorkspaceChrome.AiListeningUnderProgram);
        Assert.False(OperatorWorkspaceChrome.ShowBottomAiBar);
    }

    [Fact]
    public void Ai_listening_uses_a_muted_blue_that_is_still_on_theme()
    {
        Assert.Equal("#12202E", OperatorWorkspaceChrome.AiListeningBackground);
        Assert.Equal("#2B5A7A", OperatorWorkspaceChrome.AiListeningBorder);
        Assert.Equal("#163044", OperatorWorkspaceChrome.AiListeningHeaderBackground);
        Assert.Equal("#38BDF8", OperatorWorkspaceChrome.AiListeningAccent);
        Assert.NotEqual("#181C24", OperatorWorkspaceChrome.AiListeningBackground);
        Assert.DoesNotContain("A78BFA", OperatorWorkspaceChrome.AiListeningBackground);
    }
}
