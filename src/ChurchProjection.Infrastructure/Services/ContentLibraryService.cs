using System.Collections.Concurrent;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Parsing;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Data;
using ChurchProjection.Infrastructure.Parsing;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

public class ContentLibraryService : IContentLibraryService
{
    private readonly ScriptureRepository _scriptureRepo;
    private readonly SongRepository _songRepo;
    private readonly IBibleApiService _bibleApi;

    // L1 cache: whole chapters held in memory, keyed by "translation|book|chapter". Bible text is
    // immutable, so entries never expire. Lookups during a live service hit this first and avoid
    // both the disk and the network entirely.
    private readonly ConcurrentDictionary<string, IReadOnlyList<ScripturePassage>> _chapterCache = new();

    // Single-flight gates so concurrent requests for the same chapter share one network fetch
    // instead of stampeding the API.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _chapterFetchLocks = new();

    public ContentLibraryService(
        ScriptureRepository scriptureRepo,
        SongRepository songRepo,
        IBibleApiService bibleApi)
    {
        _scriptureRepo = scriptureRepo;
        _songRepo = songRepo;
        _bibleApi = bibleApi;
    }

    private static string ChapterKey(string translation, string book, int chapter) =>
        $"{translation}|{book}|{chapter}";

    public async Task<ScripturePassage?> GetOrFetchScriptureAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default)
    {
        var cached = await _scriptureRepo.FindAsync(
            reference.Book, reference.Chapter, reference.VerseStart, reference.VerseEnd, translation).ConfigureAwait(false);

        if (cached is not null)
        {
            Log.Debug("Scripture cache hit: {Ref}", reference);
            return cached;
        }

        var fetched = await _bibleApi.FetchPassageAsync(reference, translation, cancellationToken).ConfigureAwait(false);
        if (fetched is null) return null;

        return await _scriptureRepo.UpsertAsync(fetched).ConfigureAwait(false);
    }

    public async Task<List<ScripturePassage>> GetOrFetchVersesAsync(ScriptureReference reference, string translation = "BSB", bool localOnly = false, CancellationToken cancellationToken = default)
    {
        var key = ChapterKey(translation, reference.Book, reference.Chapter);

        // L1: in-memory chapter cache (sub-millisecond, no disk/network).
        if (_chapterCache.TryGetValue(key, out var memChapter))
            return FilterToRange(memChapter, reference);

        // L2: SQLite. Read the whole chapter at once and promote it into the L1 cache.
        var chapterVerses = await _scriptureRepo.FindAllInChapterAsync(reference.Book, reference.Chapter, translation).ConfigureAwait(false);
        if (chapterVerses.Count > 0)
        {
            var ranged = FilterToRange(chapterVerses, reference);

            // If the local chapter already satisfies the request (always true once a chapter has
            // been fully hydrated), serve it. Only refetch when we're online and genuinely missing
            // verses from the requested range (e.g. legacy partial data).
            if (localOnly || CoversRange(ranged, reference))
            {
                _chapterCache.TryAdd(key, chapterVerses);
                Log.Debug("Scripture cache hit (SQLite): {Ref} ({Count} verses)", reference, ranged.Count);
                return ranged;
            }
        }

        // L3: network. The hot live/AI path never gets here (localOnly == true).
        if (localOnly)
            return chapterVerses.Count > 0 ? FilterToRange(chapterVerses, reference) : [];

        return await HydrateChapterAsync(key, reference, translation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches and caches the entire chapter in a single coalesced network operation, so every
    /// other verse in that chapter becomes a local hit afterwards.
    /// </summary>
    private async Task<List<ScripturePassage>> HydrateChapterAsync(
        string key, ScriptureReference reference, string translation, CancellationToken cancellationToken)
    {
        var gate = _chapterFetchLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have hydrated this chapter while we waited.
            if (_chapterCache.TryGetValue(key, out var filled))
                return FilterToRange(filled, reference);

            var wholeChapter = new ScriptureReference(reference.Book, reference.Chapter, 1, ScriptureReference.WholeChapterSentinel);
            var verses = await _bibleApi.FetchVersesAsync(wholeChapter, translation, cancellationToken).ConfigureAwait(false);
            Log.Information("Hydrated chapter {Book} {Chapter} ({Translation}): {Count} verse(s)",
                reference.Book, reference.Chapter, translation, verses.Count);

            if (verses.Count == 0)
                return [];

            await _scriptureRepo.UpsertManyAsync(verses, cancellationToken).ConfigureAwait(false);

            var sorted = verses.OrderBy(v => v.VerseStart).ToList();
            _chapterCache[key] = sorted;
            return FilterToRange(sorted, reference);
        }
        finally
        {
            gate.Release();
        }
    }

    private static List<ScripturePassage> FilterToRange(IReadOnlyList<ScripturePassage> chapter, ScriptureReference reference)
    {
        var end = reference.VerseEnd ?? reference.VerseStart;
        if (reference.VerseStart <= 1 && end >= ScriptureReference.WholeChapterSentinel)
            return chapter.ToList();

        return chapter
            .Where(v => v.VerseStart >= reference.VerseStart && v.VerseStart <= end)
            .ToList();
    }

    private static bool CoversRange(List<ScripturePassage> verses, ScriptureReference reference)
    {
        if (verses.Count == 0)
            return false;

        var end = reference.VerseEnd ?? reference.VerseStart;
        if (end >= ScriptureReference.WholeChapterSentinel)
            return true; // whole-chapter request: treat the cached chapter as authoritative

        var present = verses.Select(v => v.VerseStart).ToHashSet();
        for (var v = reference.VerseStart; v <= end; v++)
        {
            if (!present.Contains(v))
                return false;
        }
        return true;
    }

    public async Task<List<ScripturePassage>> SearchScripturesAsync(string query, string translation = "BSB", CancellationToken cancellationToken = default)
    {
        var reference = ScriptureReferenceParser.TryParse(query);
        if (reference is not null)
        {
            return await GetOrFetchVersesAsync(reference, translation, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return await _scriptureRepo.SearchAsync(query, translation).ConfigureAwait(false);
    }

    public Task<List<Song>> GetAllSongsAsync(CancellationToken cancellationToken = default) => _songRepo.GetAllAsync();

    public Task<List<Song>> SearchSongsAsync(string query, CancellationToken cancellationToken = default) => _songRepo.SearchAsync(query);

    public async Task<Song> ImportSongAsync(string title, string rawLyrics, string? artist = null, CancellationToken cancellationToken = default)
    {
        var sections = SongImportParser.ParseSections(rawLyrics);
        var song = new Song
        {
            Title = title,
            Artist = artist,
            Sections = sections
        };

        return await _songRepo.InsertAsync(song).ConfigureAwait(false);
    }

    public async Task<Song> SaveSongAsync(Song song, CancellationToken cancellationToken = default)
    {
        return song.Id == 0
            ? await _songRepo.InsertAsync(song).ConfigureAwait(false)
            : await _songRepo.UpdateAsync(song).ConfigureAwait(false);
    }

    public Task DeleteSongAsync(long songId, CancellationToken cancellationToken = default) => _songRepo.DeleteAsync(songId);
}
