using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Parsing;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// A preacher often reads a chapter/verse digit-by-digit ("Psalm one zero nine" = Psalm 109). Speech
/// engines surface that as separate single-digit tokens ("1 0 9"), which the positional parser would
/// otherwise read as chapter 1 verse 9. These tests pin the conservative coalescing that glues such a
/// run back into one number while leaving an ordinary "chapter verse" pair ("3 16") alone.
/// </summary>
public class SpelledDigitReferenceTests
{
    private static (string Book, int Chapter, int VerseStart, int? VerseEnd)? Tup(ScriptureReference? r)
        => r is null ? null : (r.Book, r.Chapter, r.VerseStart, r.VerseEnd);

    [Theory]
    [InlineData("1 0 9", "109")]                 // contains a 0 and 3 digits → merged
    [InlineData("1 1 9", "119")]                 // 3-digit run, no 0 → merged
    [InlineData("1 0", "10")]                    // 2-digit run with a 0 → merged
    [InlineData("3 16", "3 16")]                 // "16" is not a single digit → left as chapter/verse
    [InlineData("3 6", "3 6")]                   // 2-digit run, no 0 → left alone (conservative)
    [InlineData("john 1 0 9 verse 5", "john 109 verse 5")] // run flushed at the cue word
    [InlineData("105 to 109", "105 to 109")]     // already-formed numbers untouched
    public void Coalesces_only_clear_digit_spelling(string input, string expected)
    {
        Assert.Equal(expected, ScriptureReferenceParser.CoalesceSpelledDigits(input));
    }

    [Fact]
    public void Builder_reads_a_digit_spelled_chapter_as_one_number()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("Psalm one zero nine", DateTimeOffset.UtcNow);
        Assert.Equal(("Psalms", 109, 1, ScriptureReference.WholeChapterSentinel), Tup(r));
    }

    [Fact]
    public void Builder_reads_the_digit_token_form_too()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("Psalm 1 0 9", DateTimeOffset.UtcNow);
        Assert.Equal(("Psalms", 109, 1, ScriptureReference.WholeChapterSentinel), Tup(r));
    }

    [Fact]
    public void Builder_reads_a_three_digit_run_without_zero()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("Psalm one one nine", DateTimeOffset.UtcNow);
        Assert.Equal(("Psalms", 119, 1, ScriptureReference.WholeChapterSentinel), Tup(r));
    }

    [Fact]
    public void Builder_reads_a_digit_spelled_chapter_with_a_following_verse()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("Psalm one zero nine verse five", DateTimeOffset.UtcNow);
        Assert.Equal(("Psalms", 109, 5, (int?)null), Tup(r));
    }

    // The conservative line: an ordinary "chapter verse" delivery must NOT be glued together.
    [Fact]
    public void Builder_keeps_an_ordinary_chapter_verse_pair_separate()
    {
        var builder = new SpokenReferenceBuilder();
        var r = builder.Accept("John three sixteen", DateTimeOffset.UtcNow);
        Assert.Equal(("John", 3, 16, (int?)null), Tup(r));
    }

    [Fact]
    public void ExtractFromSpoken_handles_a_digit_spelled_reference()
    {
        var refs = ScriptureReferenceParser.ExtractFromSpoken("turn to Psalm one zero nine verse one");
        var first = refs.Count > 0 ? refs[0] : null;
        Assert.Equal(("Psalms", 109, 1, (int?)null), Tup(first));
    }
}
