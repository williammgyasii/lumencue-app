using ChurchProjection.Core.Parsing;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// Guards the "John flashes in while typing mat 1" bug. When the operator is mid-typing a reference
/// (a book token, then heading into numbers), the typed search must NOT fall through to the semantic
/// phrase search — which surfaces unrelated central verses (John 1:1) for low-signal fragments.
/// Reference intent is signalled by a trailing space or a number after the book; a bare word is left
/// alone so genuine single-word searches still run.
/// </summary>
public class PartialReferenceTests
{
    [Theory]
    [InlineData("mat ")]       // alias + trailing space, numbers not typed yet
    [InlineData("matthew ")]   // full name + trailing space
    [InlineData("matew ")]     // typo'd book + trailing space
    [InlineData("1 john ")]    // numbered book + trailing space
    [InlineData("john ")]      // book that is also a common word, but with reference intent
    public void Flags_in_progress_reference_fragments(string input)
    {
        Assert.True(ScriptureReferenceParser.LooksLikePartialReference(input));
    }

    [Theory]
    [InlineData("mat 1 1")]    // complete verse reference
    [InlineData("mat 1")]      // complete chapter reference
    [InlineData("genesis 1")]  // complete chapter reference
    [InlineData("psalm 23")]   // complete chapter reference
    public void Does_not_flag_complete_references(string input)
    {
        Assert.False(ScriptureReferenceParser.LooksLikePartialReference(input));
    }

    [Theory]
    [InlineData("love")]                 // a real word search
    [InlineData("for god so loved")]     // a phrase search
    [InlineData("job")]                  // a book word typed bare = word search (no reference intent)
    [InlineData("grace")]
    [InlineData("")]
    [InlineData("   ")]
    public void Does_not_flag_word_or_phrase_searches(string input)
    {
        Assert.False(ScriptureReferenceParser.LooksLikePartialReference(input));
    }
}
