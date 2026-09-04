using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SongLiveSyncTests
{
    [Fact]
    public void Refreshes_when_the_saved_song_is_live_and_the_section_still_exists()
    {
        Assert.True(SongLiveSync.ShouldRefreshLive(savedSongIsLive: true, liveSectionStillExists: true));
    }

    [Fact]
    public void Skips_when_another_item_is_live()
    {
        Assert.False(SongLiveSync.ShouldRefreshLive(savedSongIsLive: false, liveSectionStillExists: true));
    }

    [Fact]
    public void Skips_when_the_song_is_not_live()
    {
        Assert.False(SongLiveSync.IsSavedSongLive(savedSongId: 5, liveSongId: null));
        Assert.False(SongLiveSync.ShouldRefreshLive(savedSongIsLive: false, liveSectionStillExists: false));
    }

    [Fact]
    public void Skips_when_the_live_section_is_gone()
    {
        Assert.False(SongLiveSync.ShouldRefreshLive(savedSongIsLive: true, liveSectionStillExists: false));
        Assert.False(SongLiveSync.TryMatch(
            new SongLiveSync.SectionKey("verse", 2, 0),
            [new("verse", 1, 0), new("chorus", 1, 0)],
            out _));
    }

    [Fact]
    public void Matches_the_same_section_and_page_after_rebuild()
    {
        Assert.True(SongLiveSync.IsSavedSongLive(savedSongId: 9, liveSongId: 9));
        Assert.True(SongLiveSync.TryMatch(
            new SongLiveSync.SectionKey("chorus", 1, 0),
            [new("verse", 1, 0), new("chorus", 1, 0), new("chorus", 1, 1)],
            out var index));
        Assert.Equal(1, index);
    }
}
