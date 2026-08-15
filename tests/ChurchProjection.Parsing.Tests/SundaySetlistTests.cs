using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SundaySetlistTests
{
    [Fact]
    public void TryAdd_SkipsDuplicatesAndBlankTitles()
    {
        var titles = new List<string> { "10,000 Reasons" };

        Assert.False(SundaySetlist.TryAdd(titles, "10,000 reasons"));
        Assert.False(SundaySetlist.TryAdd(titles, "  "));
        Assert.True(SundaySetlist.TryAdd(titles, " Amazing Grace "));
        Assert.Equal(["10,000 Reasons", "Amazing Grace"], titles);
    }

    [Fact]
    public void NextTitle_ReturnsTheFollowingSongThenStops()
    {
        var titles = new List<string> { "A", "B", "C" };

        Assert.Equal("A", SundaySetlist.NextTitle(titles, currentTitle: null));
        Assert.Equal("B", SundaySetlist.NextTitle(titles, "A"));
        Assert.Equal("C", SundaySetlist.NextTitle(titles, "b"));
        Assert.Null(SundaySetlist.NextTitle(titles, "C"));
    }

    [Fact]
    public void TryRemove_DropsTheMatchingTitle()
    {
        var titles = new List<string> { "A", "B" };

        Assert.True(SundaySetlist.TryRemove(titles, "a"));
        Assert.Equal(["B"], titles);
        Assert.False(SundaySetlist.TryRemove(titles, "missing"));
    }
}
