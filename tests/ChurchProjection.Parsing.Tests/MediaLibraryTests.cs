using ChurchProjection.Core.Models.Projection;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

// Pins the dedup rule for the media library: a file already in the library shouldn't be added
// again. Matching is by normalized full path (case-insensitive, matching Windows file systems).
public class MediaLibraryTests
{
    private static AnnouncementMedia Item(string path) => new() { Name = "x", Path = path };

    [Fact]
    public void Finds_an_existing_item_with_the_same_path()
    {
        var items = new[] { Item(@"C:\media\welcome.png"), Item(@"C:\media\clip.mp4") };

        var match = MediaLibrary.FindByPath(items, @"C:\media\welcome.png");

        Assert.NotNull(match);
        Assert.Equal(@"C:\media\welcome.png", match!.Path);
    }

    [Fact]
    public void Treats_different_casing_and_separators_as_the_same_file()
    {
        var items = new[] { Item(@"C:\media\Welcome.png") };

        // Different case + forward slashes should still resolve to the same Windows file.
        Assert.NotNull(MediaLibrary.FindByPath(items, @"c:/media/welcome.png"));
    }

    [Fact]
    public void Returns_null_for_a_genuinely_different_file()
    {
        var items = new[] { Item(@"C:\media\welcome.png") };

        Assert.Null(MediaLibrary.FindByPath(items, @"C:\media\closing.png"));
    }
}
