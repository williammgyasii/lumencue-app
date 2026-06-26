using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Search;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ScriptureParaphraseWatcherTests
{
    private const string Paraphrase = "the bible tells us to love our enemies and pray for those who hurt us";

    // A confident semantic match for a real paraphrase is surfaced.
    [Fact]
    public async Task Surfaces_a_confident_semantic_match()
    {
        var fake = new FakeSearch { Hits = [Hit("Matthew", 5, 44, 0.72, ScriptureSearchHit.KindSemantic)] };
        var watcher = new ScriptureParaphraseWatcher(fake);

        var detections = await watcher.DetectAsync(Paraphrase, "BSB");

        var detection = Assert.Single(detections);
        Assert.Equal("Matthew", detection.Passage.Book);
        Assert.Equal(44, detection.Passage.VerseStart);
    }

    // Keyword-only blends are topical coincidences, not paraphrases — dropped even at a high score.
    [Fact]
    public async Task Drops_keyword_only_matches()
    {
        var fake = new FakeSearch { Hits = [Hit("Matthew", 5, 44, 0.95, ScriptureSearchHit.KindKeyword)] };
        var watcher = new ScriptureParaphraseWatcher(fake);

        Assert.Empty(await watcher.DetectAsync(Paraphrase, "BSB"));
    }

    // A weak semantic similarity is below the conservative confidence floor.
    [Fact]
    public async Task Drops_low_confidence_matches()
    {
        var fake = new FakeSearch { Hits = [Hit("Matthew", 5, 44, 0.30, ScriptureSearchHit.KindSemantic)] };
        var watcher = new ScriptureParaphraseWatcher(fake);

        Assert.Empty(await watcher.DetectAsync(Paraphrase, "BSB"));
    }

    // Short utterances ("amen", "yes Lord") are filler — no search, no detection.
    [Fact]
    public async Task Ignores_short_utterances_without_searching()
    {
        var fake = new FakeSearch { Hits = [Hit("Matthew", 5, 44, 0.9, ScriptureSearchHit.KindSemantic)] };
        var watcher = new ScriptureParaphraseWatcher(fake);

        Assert.Empty(await watcher.DetectAsync("amen hallelujah yes", "BSB"));
        Assert.Equal(0, fake.SearchCalls);
    }

    // A pure spoken reference ("John 3:16") is handled by the AI Suggestions path, not here.
    [Fact]
    public async Task Skips_explicit_references_without_searching()
    {
        var fake = new FakeSearch { Hits = [Hit("John", 3, 16, 0.9, ScriptureSearchHit.KindSemantic)] };
        var watcher = new ScriptureParaphraseWatcher(fake);

        Assert.Empty(await watcher.DetectAsync("John 3:16", "BSB"));
        Assert.Equal(0, fake.SearchCalls);
    }

    // The same verse echoed again shortly after is de-duplicated so the lane doesn't repeat it.
    [Fact]
    public async Task Deduplicates_a_recently_detected_verse()
    {
        var fake = new FakeSearch { Hits = [Hit("Matthew", 5, 44, 0.72, ScriptureSearchHit.KindSemantic)] };
        var watcher = new ScriptureParaphraseWatcher(fake);

        Assert.Single(await watcher.DetectAsync(Paraphrase, "BSB"));
        Assert.Empty(await watcher.DetectAsync(Paraphrase, "BSB"));
    }

    private static ScriptureSearchHit Hit(string book, int chapter, int verse, double score, string kind)
        => new(
            new ScripturePassage { Book = book, Chapter = chapter, VerseStart = verse, Text = "...", Translation = "BSB" },
            score,
            kind);

    private sealed class FakeSearch : IScriptureSearchService
    {
        public List<ScriptureSearchHit> Hits { get; set; } = [];
        public int SearchCalls { get; private set; }

        public bool IsIndexReady(string translation) => true;
        public Task EnsureIndexedAsync(string translation, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<ScriptureSearchHit>> SearchAsync(string query, string translation, int maxResults = 12, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(Hits);
        }
    }
}
