using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class WorkspaceModeSnapshotTests
{
    [Fact]
    public void Returning_to_bible_keeps_the_last_chapter()
    {
        var memory = new WorkspaceModeSnapshot();
        memory.RememberBible(new WorkspaceModePlace("", "John", 3));
        memory.RememberSongs(new WorkspaceModePlace("grace", null, null));

        var restored = memory.RestoreBible();

        Assert.Equal("John", restored.BrowseBook);
        Assert.Equal(3, restored.BrowseChapter);
        Assert.NotEqual("Genesis", restored.BrowseBook);
    }

    [Fact]
    public void Returning_to_songs_keeps_the_last_search()
    {
        var memory = new WorkspaceModeSnapshot();
        memory.RememberSongs(new WorkspaceModePlace("amazing grace", null, null));
        memory.RememberBible(new WorkspaceModePlace("", "John", 3));

        var restored = memory.RestoreSongs();

        Assert.NotNull(restored);
        Assert.Equal("amazing grace", restored.Value.SearchQuery);
    }

    [Fact]
    public void First_bible_visit_opens_genesis_1()
    {
        var memory = new WorkspaceModeSnapshot();

        var restored = memory.RestoreBible();

        Assert.Equal("Genesis", restored.BrowseBook);
        Assert.Equal(1, restored.BrowseChapter);
    }

    [Fact]
    public void Media_folder_survives_a_bible_and_songs_round_trip()
    {
        var memory = new WorkspaceModeSnapshot();
        memory.RememberMediaFolder("streets");
        memory.RememberBible(new WorkspaceModePlace("", "John", 3));
        memory.RememberSongs(new WorkspaceModePlace("grace", null, null));
        memory.RestoreBible();
        memory.RestoreSongs();

        Assert.Equal("streets", memory.MediaFolderId);
    }
}
