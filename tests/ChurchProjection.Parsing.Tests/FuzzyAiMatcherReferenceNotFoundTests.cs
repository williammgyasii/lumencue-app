using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Matching;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class FuzzyAiMatcherReferenceNotFoundTests
{
    // A reference whose verse lookup is empty AND whose chapter is also empty doesn't exist at all
    // (e.g. "John 99" — John has only 21 chapters). The matcher should announce it once.
    [Fact]
    public async Task Emits_not_found_when_neither_verse_nor_chapter_exists()
    {
        var matcher = new FuzzyAiMatcherService(new EmptyLibrary());
        var seen = new List<ReferenceNotFound>();
        using var _ = matcher.ReferenceNotFound.Subscribe(seen.Add);

        await matcher.MatchAsync("John 99:5");

        Assert.Single(seen);
        Assert.Contains("John", seen[0].Reference);
    }

    // An out-of-range verse whose chapter *does* exist is not "doesn't exist" — the matcher already
    // falls back to the chapter, so no not-found should fire.
    [Fact]
    public async Task Does_not_emit_when_chapter_exists_but_verse_is_out_of_range()
    {
        var matcher = new FuzzyAiMatcherService(new ChapterOnlyLibrary());
        var seen = new List<ReferenceNotFound>();
        using var _ = matcher.ReferenceNotFound.Subscribe(seen.Add);

        await matcher.MatchAsync("John 5:99");

        Assert.Empty(seen);
    }

    // Sliding transcript windows repeat the same words; a missing reference must toast at most once
    // per short window, not on every re-match.
    [Fact]
    public async Task Deduplicates_the_same_missing_reference()
    {
        var matcher = new FuzzyAiMatcherService(new EmptyLibrary());
        var seen = new List<ReferenceNotFound>();
        using var _ = matcher.ReferenceNotFound.Subscribe(seen.Add);

        await matcher.MatchAsync("John 99:5");
        await matcher.MatchAsync("John 99:5");

        Assert.Single(seen);
    }

    /// <summary>Returns no verses for any reference — every lookup is a miss.</summary>
    private sealed class EmptyLibrary : FakeLibraryBase
    {
        public override Task<List<ScripturePassage>> GetOrFetchVersesAsync(
            ScriptureReference reference, string translation = "BSB", bool localOnly = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ScripturePassage>());
    }

    /// <summary>Returns a verse only for whole-chapter requests, mimicking a chapter that exists but
    /// whose requested verse is out of range.</summary>
    private sealed class ChapterOnlyLibrary : FakeLibraryBase
    {
        public override Task<List<ScripturePassage>> GetOrFetchVersesAsync(
            ScriptureReference reference, string translation = "BSB", bool localOnly = false,
            CancellationToken cancellationToken = default)
        {
            var isWholeChapter = reference.VerseEnd.HasValue
                && reference.VerseEnd.Value >= ScriptureReference.WholeChapterSentinel;

            var verses = isWholeChapter
                ? new List<ScripturePassage>
                {
                    new() { Book = reference.Book, Chapter = reference.Chapter, VerseStart = 1, Text = "In the beginning" }
                }
                : new List<ScripturePassage>();

            return Task.FromResult(verses);
        }
    }

    /// <summary>Throws for everything the not-found tests don't exercise.</summary>
    private abstract class FakeLibraryBase : IContentLibraryService
    {
        public abstract Task<List<ScripturePassage>> GetOrFetchVersesAsync(
            ScriptureReference reference, string translation = "BSB", bool localOnly = false,
            CancellationToken cancellationToken = default);

        public Task<List<ScripturePassage>> SearchScripturesAsync(string query, string translation = "BSB", CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<ScripturePassage?> GetOrFetchScriptureAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<List<Song>> GetAllSongsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<List<Song>> SearchSongsAsync(string query, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Song> ImportSongAsync(string title, string rawLyrics, string? artist = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Song> SaveSongAsync(Song song, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task DeleteSongAsync(long songId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
