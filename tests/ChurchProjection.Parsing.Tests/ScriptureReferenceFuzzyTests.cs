using ChurchProjection.Core.Parsing;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// Typo tolerance for the TYPED search box only. An operator in a rush may drop or duplicate a
/// letter ("matew 1 1"); fuzzy book resolution should recover the intended book. The default
/// (non-fuzzy) parse — used by the live spoken/AI path — must stay strict so free speech never
/// gets "corrected" into a false reference.
/// </summary>
public class ScriptureReferenceFuzzyTests
{
    [Theory]
    [InlineData("matew 1 1", "Matthew", 1, 1)]      // dropped 't'
    [InlineData("mathew 1 1", "Matthew", 1, 1)]     // dropped 't' (other spot)
    [InlineData("marrk 1 1", "Mark", 1, 1)]         // doubled 'r'
    [InlineData("jhn 3 16", "John", 3, 16)]         // dropped 'o'
    [InlineData("romns 8 28", "Romans", 8, 28)]     // dropped 'a'
    public void Corrects_single_letter_typos_when_fuzzy_enabled(string input, string book, int chapter, int verse)
    {
        var reference = ScriptureReferenceParser.TryParse(input, allowFuzzyBook: true);

        Assert.NotNull(reference);
        Assert.Equal(book, reference!.Book);
        Assert.Equal(chapter, reference.Chapter);
        Assert.Equal(verse, reference.VerseStart);
    }

    [Theory]
    [InlineData("matthew 1 1", "Matthew")]  // full name
    [InlineData("mat 1 1", "Matthew")]      // alias
    [InlineData("genesis 1 1", "Genesis")]  // full name
    [InlineData("john 3 16", "John")]       // full name
    public void Still_resolves_correct_spellings(string input, string book)
    {
        var reference = ScriptureReferenceParser.TryParse(input, allowFuzzyBook: true);

        Assert.NotNull(reference);
        Assert.Equal(book, reference!.Book);
    }

    // The default overload (and thus the live spoken path) must NOT fuzzy-correct.
    [Fact]
    public void Default_parse_stays_strict_and_ignores_typos()
    {
        Assert.Null(ScriptureReferenceParser.TryParse("matew 1 1"));
        Assert.Null(ScriptureReferenceParser.TryParse("jhn 3 16"));
    }

    // Tokens too short to disambiguate, or genuinely not a book, must never be force-fit to a book.
    [Theory]
    [InlineData("jo 1 1")]      // 2 chars: ambiguous between John/Joel/Jonah/Job
    [InlineData("xyz 1 1")]     // not a book at all
    [InlineData("zzzz 1 1")]    // gibberish, not within edit distance of any book
    public void Does_not_invent_a_book_from_short_or_unrelated_tokens(string input)
    {
        Assert.Null(ScriptureReferenceParser.TryParse(input, allowFuzzyBook: true));
    }
}
