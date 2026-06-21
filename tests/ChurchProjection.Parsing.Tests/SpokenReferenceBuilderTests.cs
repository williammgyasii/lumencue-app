using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Parsing;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SpokenReferenceBuilderTests
{
    private static (string Book, int Chapter, int VerseStart, int? VerseEnd)? Tup(ScriptureReference? r)
        => r is null ? null : (r.Book, r.Chapter, r.VerseStart, r.VerseEnd);

    // The headline case: the preacher says the book, pauses ~15s, says the chapter, pauses ~15s,
    // then the verse. The builder should surface the whole chapter first, then refine to the verse.
    [Fact]
    public void Assembles_reference_spoken_in_fragments_across_long_pauses()
    {
        var builder = new SpokenReferenceBuilder(TimeSpan.FromSeconds(30));
        var t = DateTimeOffset.UtcNow;

        // Naming the book surfaces its opening verse at once (Matthew 1:1) so the operator can load it
        // immediately and navigate from there as the chapter/verse follow.
        Assert.Equal(("Matthew", 1, 1, (int?)null), Tup(builder.Accept("let's turn to Matthew", t)));

        // ~15s later: "chapter two" → show the whole chapter.
        t = t.AddSeconds(15);
        var afterChapter = builder.Accept("chapter two", t);
        Assert.Equal(("Matthew", 2, 1, ScriptureReference.WholeChapterSentinel), Tup(afterChapter));

        // ~15s later: "verse number three" → refine to Matthew 2:3.
        t = t.AddSeconds(15);
        var afterVerse = builder.Accept("verse number three", t);
        Assert.Equal(("Matthew", 2, 3, (int?)null), Tup(afterVerse));
    }

    [Fact]
    public void Expires_pending_reference_after_the_gap_timeout()
    {
        var builder = new SpokenReferenceBuilder(TimeSpan.FromSeconds(30));
        var t = DateTimeOffset.UtcNow;

        // Naming the book surfaces Matthew 1:1 right away…
        Assert.Equal(("Matthew", 1, 1, (int?)null), Tup(builder.Accept("Matthew", t)));

        // …but the chapter arrives too late — the stale book has expired, so nothing is fabricated.
        t = t.AddSeconds(31);
        Assert.Null(builder.Accept("chapter two", t));
    }

    [Fact]
    public void Ignores_bare_numbers_with_no_pending_book()
    {
        var builder = new SpokenReferenceBuilder();
        Assert.Null(builder.Accept("chapter two verse three", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void New_book_restarts_the_pending_reference()
    {
        var builder = new SpokenReferenceBuilder();
        var t = DateTimeOffset.UtcNow;

        Assert.NotNull(builder.Accept("John chapter three", t));
        // Switching books mid-thought drops the old chapter and surfaces the new book's opening verse.
        Assert.Equal(("Romans", 1, 1, (int?)null), Tup(builder.Accept("actually Romans", t.AddSeconds(3))));
        var romans = builder.Accept("chapter eight verse one", t.AddSeconds(6));
        Assert.Equal(("Romans", 8, 1, (int?)null), Tup(romans));
    }

    [Fact]
    public void Handles_a_contiguous_reference_in_a_single_utterance()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("John 3:16", DateTimeOffset.UtcNow);
        Assert.Equal(("John", 3, 16, (int?)null), Tup(r));
    }

    [Fact]
    public void Captures_a_spoken_verse_range_with_to()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("Matthew chapter five verse one to seven", DateTimeOffset.UtcNow);
        Assert.Equal(("Matthew", 5, 1, (int?)7), Tup(r));
    }

    [Fact]
    public void Captures_a_spoken_verse_range_with_through()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("John chapter three verses sixteen through eighteen", DateTimeOffset.UtcNow);
        Assert.Equal(("John", 3, 16, (int?)18), Tup(r));
    }

    [Fact]
    public void Assembles_a_verse_range_spoken_across_pauses()
    {
        var builder = new SpokenReferenceBuilder(TimeSpan.FromSeconds(30));
        var t = DateTimeOffset.UtcNow;

        Assert.Equal(("Romans", 8, 1, ScriptureReference.WholeChapterSentinel),
            Tup(builder.Accept("Romans chapter eight", t)));

        // The verse start arrives first…
        t = t.AddSeconds(10);
        Assert.Equal(("Romans", 8, 28, (int?)null), Tup(builder.Accept("verse twenty eight", t)));

        // …then the range end after a pause.
        t = t.AddSeconds(8);
        Assert.Equal(("Romans", 8, 28, (int?)30), Tup(builder.Accept("to thirty", t)));
    }

    [Fact]
    public void Assembles_a_range_when_the_connector_and_end_arrive_in_separate_utterances()
    {
        var builder = new SpokenReferenceBuilder(TimeSpan.FromSeconds(30));
        var t = DateTimeOffset.UtcNow;

        // Deepgram often finalises "...verse one to" and the trailing number as two segments.
        Assert.Equal(("John", 1, 1, (int?)null), Tup(builder.Accept("John chapter 1 verse 1 to", t)));

        t = t.AddSeconds(1);
        Assert.Equal(("John", 1, 1, (int?)5), Tup(builder.Accept("5", t)));
    }

    [Fact]
    public void Reads_a_hyphenated_range_token()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("Genesis 1:1-3", DateTimeOffset.UtcNow);
        Assert.Equal(("Genesis", 1, 1, (int?)3), Tup(r));
    }

    [Fact]
    public void Does_not_re_emit_the_same_reference_twice()
    {
        var builder = new SpokenReferenceBuilder();
        var t = DateTimeOffset.UtcNow;

        Assert.NotNull(builder.Accept("Psalm twenty three", t));
        // Repeating it (a common preacher habit) should not spam a duplicate suggestion.
        Assert.Null(builder.Accept("Psalm twenty three", t.AddSeconds(2)));
    }
}
