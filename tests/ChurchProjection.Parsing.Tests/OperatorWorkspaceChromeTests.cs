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

    [Fact]
    public void Ai_listening_clips_to_the_rail_and_transcript_scrolls()
    {
        Assert.True(OperatorWorkspaceChrome.AiListeningClipsToRail);
        Assert.True(OperatorWorkspaceChrome.TranscriptScrollsInsideBox);
        Assert.True(OperatorWorkspaceChrome.TranscriptStickToLatest);
        Assert.True(OperatorWorkspaceChrome.TranscriptFillsRemainingRail);
        Assert.False(OperatorWorkspaceChrome.ShowLastHeardInExpandedPanel);
        Assert.True(OperatorWorkspaceChrome.TranscriptBoxMinHeight > 40);
        Assert.True(OperatorWorkspaceChrome.AiListeningMinBodyHeight > OperatorWorkspaceChrome.TranscriptBoxMinHeight);
        Assert.True(OperatorWorkspaceChrome.ProgramPreviewMaxHeight(500)
                    <= 500 - OperatorWorkspaceChrome.AiListeningMinBodyHeight);
    }

    [Fact]
    public void Now_singing_toolbar_lives_on_the_tab_row_with_a_title_bubble()
    {
        Assert.True(OperatorWorkspaceChrome.NowSingingToolbarInTabStrip);
        Assert.False(OperatorWorkspaceChrome.NowSingingShowsInnerTitleBar);
        Assert.True(OperatorWorkspaceChrome.NowSingingTitleIsBubble);
        Assert.False(OperatorWorkspaceChrome.NowSingingToolbarOnSongsTab);
        Assert.Equal("#3B1D3A", OperatorWorkspaceChrome.NowSingingTitleBubbleBackground);
        Assert.Equal("#F9A8D4", OperatorWorkspaceChrome.NowSingingTitleBubbleForeground);
        Assert.Equal("#F472B6", OperatorWorkspaceChrome.NowSingingTitleBubbleBorder);
        Assert.NotEqual("#ECEEF2", OperatorWorkspaceChrome.NowSingingTitleBubbleForeground);
        Assert.NotEqual("#181C24", OperatorWorkspaceChrome.NowSingingTitleBubbleBackground);
        Assert.True(OperatorWorkspaceChrome.NowSingingTitleBubbleMaxWidth >= 160);
    }

    [Fact]
    public void Scripture_cards_fill_three_columns_and_hug_height()
    {
        Assert.True(OperatorWorkspaceChrome.ScriptureCardsHugContent);
        Assert.True(OperatorWorkspaceChrome.ScriptureUsesWrapPanel);
        Assert.Equal(3, OperatorWorkspaceChrome.ScriptureGridColumns);
        var pane = 616;
        var width = OperatorWorkspaceChrome.ScriptureCardWidth(pane);
        Assert.True(width >= OperatorWorkspaceChrome.ScriptureCardMinWidth);
        Assert.True(width * 3
                    + OperatorWorkspaceChrome.ScriptureCardMarginX * 3
                    + OperatorWorkspaceChrome.ScriptureListPaddingX
                    <= pane - 1);
        Assert.True(width > pane / 4);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(616)]
    [InlineData(900)]
    [InlineData(1400)]
    public void Three_scripture_cards_still_fit_after_the_pane_resizes(double pane)
    {
        var width = OperatorWorkspaceChrome.ScriptureCardWidth(pane);
        var used = width * 3
                   + OperatorWorkspaceChrome.ScriptureCardMarginX * 3
                   + OperatorWorkspaceChrome.ScriptureListPaddingX;
        Assert.True(used <= pane - 1, $"used {used} pane {pane}");
        Assert.True(OperatorWorkspaceChrome.ScriptureCardWidth(pane + 200) > width);
    }

    [Fact]
    public void Now_singing_cards_hug_content_instead_of_filling_the_pane()
    {
        Assert.True(OperatorWorkspaceChrome.NowSingingCardsHugContent);
        Assert.True(OperatorWorkspaceChrome.NowSingingUsesWrapPanel);
    }

    [Fact]
    public void Suggestions_tab_hides_start_when_off_and_paraphrases_is_gone()
    {
        Assert.False(OperatorWorkspaceChrome.SuggestionsTabShowsStartWhenOff);
        Assert.False(OperatorWorkspaceChrome.ShowParaphrasesSubtab);
    }
}
