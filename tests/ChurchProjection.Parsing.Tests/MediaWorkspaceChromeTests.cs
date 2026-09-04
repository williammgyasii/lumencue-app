using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class MediaWorkspaceChromeTests
{
    [Fact]
    public void Collapses_utility_bars_in_media_mode()
    {
        Assert.False(MediaWorkspaceChrome.UtilityBarsExpanded(isMediaMode: true));
    }

    [Fact]
    public void Expands_utility_bars_in_bible_or_songs()
    {
        Assert.True(MediaWorkspaceChrome.UtilityBarsExpanded(isMediaMode: false));
    }
}
