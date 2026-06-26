using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ChurchProjection.Core.Bible;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Bible;

public partial class ApiBibleClient : IBibleApiService
{
    private const int OpenEndedVerse = 999;

    private readonly HttpClient _http;

    // Matches a verse-number marker, including a grouped span like "[1-3]" that paraphrase
    // translations (MSG, sometimes AMP/NLT) emit when several verses are merged into one block.
    [GeneratedRegex(@"\[(\d+)(?:-(\d+))?\]")]
    private static partial Regex VerseMarkerRegex();

    // Guards against a malformed marker (e.g. "[1-200]") fanning out into an absurd number of rows.
    private const int MaxGroupSpan = 50;

    // Confirmed bible_ids for the API.Bible Pro plan (verified live against /v1/bibles).
    private static readonly Dictionary<string, string> TranslationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KJV"]  = "de4e12af7f28f599-02",
        ["NIV"]  = "78a9f6124f344018-01",
        ["NKJV"] = "63097d2a0a2f7db3-01",
        ["NLT"]  = "d6e14a625393b4da-01",
        ["MSG"]  = "6f11a7de016f942e-01",
        ["AMP"]  = "a81b73293d3080c9-01",
        ["CSB"]  = "a556c5305ee15c3f-01",
        ["BSB"]  = "bba9f40183526463-01",
    };

    /// <summary>
    /// The HTTP client must be preconfigured with a base address and authentication. In production
    /// this points at the cloud API's <c>/bible/</c> proxy (seat-token auth, key stays server-side);
    /// relative paths (<c>bibles/...</c>) are forwarded verbatim to api.bible by the backend.
    /// </summary>
    public ApiBibleClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ScripturePassage?> FetchPassageAsync(ScriptureReference reference, string translation = "KJV", CancellationToken cancellationToken = default)
    {
        var verses = await FetchVersesAsync(reference, translation, cancellationToken).ConfigureAwait(false);
        if (verses.Count == 0) return null;
        if (verses.Count == 1) return verses[0];

        return new ScripturePassage
        {
            Translation = translation,
            Book = reference.Book,
            Chapter = reference.Chapter,
            VerseStart = reference.VerseStart,
            VerseEnd = reference.VerseEnd,
            Text = string.Join(" ", verses.Select(v => v.Text)),
        };
    }

    public async Task<List<ScripturePassage>> FetchVersesAsync(ScriptureReference reference, string translation = "KJV", CancellationToken cancellationToken = default)
    {
        if (!TranslationIds.TryGetValue(translation, out var bibleId))
        {
            Log.Warning("Translation {Translation} not available on API.Bible", translation);
            return [];
        }

        if (!BibleBooks.TryGetId(reference.Book, out var bookId))
        {
            Log.Warning("Unknown book: {Book}", reference.Book);
            return [];
        }

        var chapterId = $"{bookId}.{reference.Chapter}";
        var allVerses = await FetchChapterAsync(bibleId, chapterId, reference.Book, reference.Chapter, translation, cancellationToken).ConfigureAwait(false);
        if (allVerses.Count == 0)
            return [];

        var isWholeChapter = reference.VerseEnd is >= ScriptureReference.WholeChapterSentinel;
        var verseEnd = isWholeChapter ? OpenEndedVerse : (reference.VerseEnd ?? reference.VerseStart);

        return allVerses
            .Where(v => v.VerseStart >= reference.VerseStart && v.VerseStart <= verseEnd)
            .OrderBy(v => v.VerseStart)
            .ToList();
    }

    /// <summary>True if this translation is served by API.Bible (used to drive bulk caching).</summary>
    public bool Supports(string translation) => TranslationIds.ContainsKey(translation);

    /// <summary>
    /// Lists every real chapter id (e.g. "GEN.1") for a translation in one request, excluding the
    /// per-book ".intro" pseudo-chapters. Drives a full offline download.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetChapterIdsAsync(string translation, CancellationToken cancellationToken = default)
    {
        if (!TranslationIds.TryGetValue(translation, out var bibleId))
            return [];

        try
        {
            var response = await _http.GetAsync($"bibles/{bibleId}/books?include-chapters=true", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("API.Bible books list returned {Status} for {Translation}", response.StatusCode, translation);
                return [];
            }

            var json = await response.Content.ReadFromJsonAsync<BooksResponse>(cancellationToken).ConfigureAwait(false);
            if (json?.Data is null)
                return [];

            return json.Data
                .SelectMany(b => b.Chapters ?? [])
                .Select(c => c.Id)
                .Where(id => !string.IsNullOrEmpty(id) && !id.EndsWith(".intro", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to list chapters for {Translation}", translation);
            return [];
        }
    }

    /// <summary>Fetches and parses a whole chapter by its API.Bible id (e.g. "JHN.3").</summary>
    public Task<List<ScripturePassage>> FetchChapterByIdAsync(string translation, string chapterId, CancellationToken cancellationToken = default)
    {
        if (!TranslationIds.TryGetValue(translation, out var bibleId))
            return Task.FromResult(new List<ScripturePassage>());

        var parts = chapterId.Split('.');
        var book = BibleBooks.GetName(parts[0]);
        var chapter = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        return FetchChapterAsync(bibleId, chapterId, book, chapter, translation, cancellationToken);
    }

    private async Task<List<ScripturePassage>> FetchChapterAsync(
        string bibleId, string chapterId, string book, int chapter, string translation, CancellationToken cancellationToken)
    {
        try
        {
            // One request returns the whole chapter (its text carries [n] verse markers), instead
            // of one HTTP call per verse.
            var chapterUrl = $"bibles/{bibleId}/chapters/{chapterId}?content-type=text&include-verse-numbers=true&include-notes=false&include-titles=false&include-chapter-numbers=false&include-verse-spans=false";

            var response = await _http.GetAsync(chapterUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("API.Bible chapter fetch returned {Status} for {Chapter}", response.StatusCode, chapterId);
                return [];
            }

            var json = await response.Content.ReadFromJsonAsync<SingleVerseResponse>(cancellationToken).ConfigureAwait(false);
            if (json?.Data is null || string.IsNullOrWhiteSpace(json.Data.Content))
                return [];

            return ParseChapterContent(json.Data.Content, book, chapter, translation, chapterId);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch chapter {Chapter} ({Translation})", chapterId, translation);
            return [];
        }
    }

    /// <summary>
    /// Splits a chapter's text content into one <see cref="ScripturePassage"/> per verse. A grouped
    /// marker like "[1-3]" (used by paraphrase translations such as MSG, where verses are merged into
    /// a single block) is expanded into a row for each verse in the span — every row carrying that
    /// block's text. This keeps each row a normal single verse, so asking for any verse inside the
    /// group ("Psalm 109:2") still resolves to the group's text instead of falling back to the whole
    /// chapter. Public for unit testing of the marker parsing.
    /// </summary>
    public static List<ScripturePassage> ParseChapterContent(string content, string book, int chapter, string translation, string chapterId)
    {
        var matches = VerseMarkerRegex().Matches(content);
        if (matches.Count == 0)
            return [];

        var verses = new List<ScripturePassage>(matches.Count);
        for (int i = 0; i < matches.Count; i++)
        {
            if (!int.TryParse(matches[i].Groups[1].Value, out var verseStart))
                continue;

            var verseEnd = verseStart;
            if (matches[i].Groups[2].Success
                && int.TryParse(matches[i].Groups[2].Value, out var groupEnd)
                && groupEnd >= verseStart
                && groupEnd - verseStart <= MaxGroupSpan)
            {
                verseEnd = groupEnd;
            }

            var textStart = matches[i].Index + matches[i].Length;
            var textEnd = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var text = CleanText(content[textStart..textEnd]);
            if (text.Length == 0)
                continue;

            for (var verseNum = verseStart; verseNum <= verseEnd; verseNum++)
            {
                verses.Add(new ScripturePassage
                {
                    Translation = translation,
                    Book = book,
                    Chapter = chapter,
                    VerseStart = verseNum,
                    VerseEnd = null,
                    Text = text,
                    ApiBibleId = $"{chapterId}.{verseNum}",
                });
            }
        }

        return verses;
    }

    public Task<List<string>> GetAvailableTranslationsAsync()
        => Task.FromResult(TranslationIds.Keys.ToList());

    private static string CleanText(string raw)
    {
        var text = Regex.Replace(raw, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"\[\d+(?:-\d+)?\]", "");
        text = Regex.Replace(text, @"^[¶\s]+", "");
        return text.Trim();
    }

    private class BooksResponse
    {
        [JsonPropertyName("data")]
        public List<BookData>? Data { get; set; }
    }

    private class BookData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("chapters")]
        public List<ChapterRef>? Chapters { get; set; }
    }

    private class ChapterRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
    }

    private class SingleVerseResponse
    {
        [JsonPropertyName("data")]
        public SingleVerseData? Data { get; set; }
    }

    private class SingleVerseData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = "";
    }
}
