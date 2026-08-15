using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ScriptureRangeBookmarkTests
{
    [Fact]
    public void FromItems_CombinesAVerseSpan()
    {
        var bookmark = ScriptureRangeBookmark.FromItems(
        [
            Verse("Genesis", 1, 3, "And God said, Let there be light"),
            Verse("Genesis", 1, 4, "And God saw the light"),
            Verse("Genesis", 1, 5, "And God called the light Day"),
        ]);

        Assert.Equal("Genesis 1:3-5", bookmark.Title);
        Assert.Equal("scripture:Genesis:1:3-5", bookmark.ContentId);
        Assert.Contains("Let there be light", bookmark.Body);
        Assert.Contains("called the light Day", bookmark.Body);
        Assert.Equal("Genesis 1:3-5 (BSB)", bookmark.Footer);
        Assert.True(bookmark.IsBookmarked);
        Assert.Equal("scripture_reference", bookmark.MatchType);
    }

    [Fact]
    public void FromItems_SingleVerse_KeepsTheExistingIdShape()
    {
        var bookmark = ScriptureRangeBookmark.FromItems(
            [Verse("John", 3, 16, "For God so loved the world")]);

        Assert.Equal("John 3:16", bookmark.Title);
        Assert.Equal("scripture:John:3:16", bookmark.ContentId);
        Assert.Equal("For God so loved the world", bookmark.Body);
    }

    private static ContentItem Verse(string book, int chapter, int verse, string text) => new()
    {
        Type = ContentItemType.Scripture,
        Title = $"{book} {chapter}:{verse}",
        Body = text,
        Tag = "BSB",
        Footer = $"{book} {chapter}:{verse} (BSB)",
        Source = new ScripturePassage
        {
            Book = book,
            Chapter = chapter,
            VerseStart = verse,
            Translation = "BSB",
            Text = text,
        },
    };
}
