using System.Linq;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Parsing;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// Typed Scripture-tab expander. Spoken <see cref="ScriptureReferenceParser.TryParse(string)"/>
/// stays strict so these shortcuts never fire on free speech.
/// </summary>
public class ScriptureReferenceTypedQueryTests
{
    [Fact]
    public void Spoken_parse_does_not_accept_compact_john3_16()
    {
        Assert.Null(ScriptureReferenceParser.TryParse("John3:16"));
    }

    [Fact]
    public void Compact_book_and_chapter_is_john_3_16()
    {
        var slices = ScriptureReferenceParser.TryParseTypedQuery("John3:16");

        var one = Assert.Single(slices);
        Assert.Equal("John", one.Book);
        Assert.Equal(3, one.Chapter);
        Assert.Equal(16, one.VerseStart);
        Assert.True(one.VerseEnd is null or 16);
    }

    [Theory]
    [InlineData("I John 3:16")]
    [InlineData("1st John 3:16")]
    public void Numbered_prefix_is_first_john(string query)
    {
        var slices = ScriptureReferenceParser.TryParseTypedQuery(query);

        var one = Assert.Single(slices);
        Assert.Equal("1 John", one.Book);
        Assert.Equal(3, one.Chapter);
        Assert.Equal(16, one.VerseStart);
    }

    [Fact]
    public void Comma_list_returns_each_listed_verse_not_the_gap()
    {
        var slices = ScriptureReferenceParser.TryParseTypedQuery("John 3:16,18");

        Assert.Equal(2, slices.Count);
        Assert.Equal(("John", 3, 16), (slices[0].Book, slices[0].Chapter, slices[0].VerseStart));
        Assert.Equal(("John", 3, 18), (slices[1].Book, slices[1].Chapter, slices[1].VerseStart));
    }

    [Fact]
    public void Cross_chapter_range_covers_john_3_remainder_and_john_4_through_2()
    {
        var slices = ScriptureReferenceParser.TryParseTypedQuery("John 3:16-4:2");

        Assert.Equal(2, slices.Count);
        Assert.Equal("John", slices[0].Book);
        Assert.Equal(3, slices[0].Chapter);
        Assert.Equal(16, slices[0].VerseStart);
        Assert.Equal(ScriptureReference.WholeChapterSentinel, slices[0].VerseEnd);
        Assert.Equal(("John", 4, 1, 2), (slices[1].Book, slices[1].Chapter, slices[1].VerseStart, slices[1].VerseEnd));
    }

    [Fact]
    public void Compact_john3_without_colon_is_still_typing()
    {
        Assert.Empty(ScriptureReferenceParser.TryParseTypedQuery("John3"));
        Assert.True(ScriptureReferenceParser.LooksLikePartialReference("John3"));
    }

    [Fact]
    public void Spaced_john_3_is_still_the_chapter()
    {
        var slices = ScriptureReferenceParser.TryParseTypedQuery("John 3");

        var one = Assert.Single(slices);
        Assert.Equal("John", one.Book);
        Assert.Equal(3, one.Chapter);
        Assert.Equal(1, one.VerseStart);
        Assert.Equal(ScriptureReference.WholeChapterSentinel, one.VerseEnd);
    }

    [Fact]
    public void Phrase_is_not_a_typed_reference()
    {
        Assert.Empty(ScriptureReferenceParser.TryParseTypedQuery("love"));
        Assert.Empty(ScriptureReferenceParser.TryParseTypedQuery("for god so loved the world"));
    }

    [Fact]
    public void Fetch_concatenates_slices_in_typed_order()
    {
        var slices = ScriptureReferenceParser.TryParseTypedQuery("John 3:16,18");

        var combined = TypedScriptureSearch.FetchSlices<string>(
            slices,
            slice => new[] { $"{slice.Book} {slice.Chapter}:{slice.VerseStart}" });

        Assert.Equal(new[] { "John 3:16", "John 3:18" }, combined);
    }
}
