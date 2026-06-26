using ChurchProjection.Infrastructure.Bible;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// API.Bible returns a chapter as one text blob with inline verse-number markers. Paraphrase
/// translations (MSG especially) merge several verses into one block and mark it as a span, e.g.
/// "[1-3]". The parser must expand such a span into a row per verse so that asking for any verse in
/// the group resolves to the group's text — instead of the group being skipped and the lookup
/// collapsing to the whole chapter.
/// </summary>
public class ApiBibleChapterParsingTests
{
    [Fact]
    public void Expands_a_grouped_verse_span_into_one_row_per_verse()
    {
        const string content = "[1-3] The whole earth was one language. [4] So they said.";

        var verses = ApiBibleClient.ParseChapterContent(content, "Genesis", 11, "MSG", "GEN.11");

        Assert.Equal(4, verses.Count);
        Assert.Equal([1, 2, 3, 4], verses.Select(v => v.VerseStart).ToArray());

        // Each verse in the 1-3 group carries that block's text…
        Assert.Equal("The whole earth was one language.", verses[0].Text);
        Assert.Equal("The whole earth was one language.", verses[1].Text);
        Assert.Equal("The whole earth was one language.", verses[2].Text);
        // …and verse 4 is its own block.
        Assert.Equal("So they said.", verses[3].Text);

        // Every row stays a single verse (VerseEnd null) so the existing cache/search path is happy.
        Assert.All(verses, v => Assert.Null(v.VerseEnd));
        Assert.All(verses, v => Assert.Equal("MSG", v.Translation));
    }

    [Fact]
    public void Asking_for_a_verse_inside_a_group_resolves_to_the_group_text()
    {
        const string content = "[1-2] First block. [3-5] Second block.";

        var verses = ApiBibleClient.ParseChapterContent(content, "Psalms", 109, "MSG", "PSA.109");

        var verseFour = verses.SingleOrDefault(v => v.VerseStart == 4);
        Assert.NotNull(verseFour);
        Assert.Equal("Second block.", verseFour!.Text);
    }

    [Fact]
    public void Still_parses_ordinary_single_verse_markers()
    {
        const string content = "[1] In the beginning God created. [2] And the earth was formless.";

        var verses = ApiBibleClient.ParseChapterContent(content, "Genesis", 1, "KJV", "GEN.1");

        Assert.Equal(2, verses.Count);
        Assert.Equal("In the beginning God created.", verses[0].Text);
        Assert.Equal("And the earth was formless.", verses[1].Text);
    }

    [Fact]
    public void Strips_a_stray_span_marker_left_inside_the_text()
    {
        const string content = "[1] Start [2-3] middle that bled in.";

        var verses = ApiBibleClient.ParseChapterContent(content, "John", 1, "MSG", "JHN.1");

        Assert.DoesNotContain("[2-3]", verses[0].Text);
    }

    [Fact]
    public void Ignores_an_implausibly_large_span_by_treating_it_as_a_single_verse()
    {
        const string content = "[1-200] runaway marker.";

        var verses = ApiBibleClient.ParseChapterContent(content, "Genesis", 1, "MSG", "GEN.1");

        Assert.Single(verses);
        Assert.Equal(1, verses[0].VerseStart);
    }
}
