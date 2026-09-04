using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ScriptureSearchRankerTests
{
    [Fact]
    public void Famous_phrase_puts_John_3_16_first()
    {
        var john316 = Verse("John", 3, 16,
            "For God so loved the world that He gave His one and only Son, that everyone who believes in Him shall not perish but have eternal life.");
        var distractor = Verse("Psalms", 23, 1, "The LORD is my shepherd; I shall not want.");

        var hits = ScriptureSearchRanker.Rank(
            "god so loved the world",
            keywordCandidates: [john316],
            semanticCandidates: [(distractor, 0.90)]);

        var first = Assert.Single(hits.Take(1));
        Assert.Equal("John", first.Passage.Book);
        Assert.Equal(3, first.Passage.Chapter);
        Assert.Equal(16, first.Passage.VerseStart);
    }

    [Fact]
    public void Phrase_hit_outranks_high_cosine_semantic_only()
    {
        var phrase = Verse("John", 3, 16, "For God so loved the world that He gave His one and only Son.");
        var semanticOnly = Verse("1 John", 4, 8, "Whoever does not love does not know God, because God is love.");

        var hits = ScriptureSearchRanker.Rank(
            "god so loved the world",
            keywordCandidates: [phrase],
            semanticCandidates: [(semanticOnly, 0.90)]);

        Assert.Equal("John", hits[0].Passage.Book);
        Assert.Equal(16, hits[0].Passage.VerseStart);
        Assert.Contains(hits, h => h.Passage.Book == "1 John");
        Assert.True(hits.FindIndex(h => h.Passage.Book == "John") <
                    hits.FindIndex(h => h.Passage.Book == "1 John"));
    }

    [Fact]
    public void God_inside_godly_is_not_a_keyword_match()
    {
        Assert.False(ScriptureSearchRanker.HasKeyword(
            "training us to live self-controlled, upright, and godly lives",
            "god"));
    }

    [Fact]
    public void Semantic_only_below_floor_is_omitted()
    {
        var weak = Verse("Romans", 5, 1, "Therefore, since we have been justified through faith, we have peace with God.");

        var hits = ScriptureSearchRanker.Rank(
            "unrelated topic",
            keywordCandidates: [],
            semanticCandidates: [(weak, 0.30)]);

        Assert.Empty(hits);
    }

    private static ScripturePassage Verse(string book, int chapter, int verse, string text) => new()
    {
        Book = book,
        Chapter = chapter,
        VerseStart = verse,
        Text = text,
        Translation = "BSB",
    };
}
