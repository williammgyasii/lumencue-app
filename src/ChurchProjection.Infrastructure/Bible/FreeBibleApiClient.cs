using System.Text.Json;
using ChurchProjection.Core.Bible;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Bible;

public class FreeBibleApiClient : IBibleApiService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http = BibleHttpClients.Helloao;
    private readonly BibleCacheService? _cacheService;

    private static readonly Dictionary<string, string> FallbackTranslationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BSB"] = "BSB",
        ["KJV"] = "eng_kjv",
        ["ASV"] = "eng_asv",
        ["WEB"] = "ENGWEBP",
        ["NET"] = "eng_net",
        ["YLT"] = "eng_ylt",
        ["LSV"] = "eng_lsv",
    };

    public FreeBibleApiClient(BibleCacheService? cacheService = null)
    {
        _cacheService = cacheService;
    }

    private string? ResolveTranslationId(string translation)
    {
        var id = _cacheService?.ResolveApiId(translation);
        if (id is not null) return id;
        return FallbackTranslationIds.TryGetValue(translation, out var fallback) ? fallback : null;
    }

    public async Task<ScripturePassage?> FetchPassageAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default)
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

    public async Task<List<ScripturePassage>> FetchVersesAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default)
    {
        var translationId = ResolveTranslationId(translation);
        if (translationId is null)
        {
            Log.Warning("Translation {Translation} not available on free API", translation);
            return [];
        }

        if (!BibleBooks.TryGetId(reference.Book, out var bookId))
        {
            Log.Warning("Unknown book: {Book}", reference.Book);
            return [];
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);
            var ct = timeoutCts.Token;

            var url = $"{translationId}/{bookId}/{reference.Chapter}.json";
            Log.Debug("Fetching: {Url}", url);

            var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Free Bible API returned {Status}", response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (json.TrimStart().StartsWith('<'))
            {
                Log.Warning("Free Bible API returned HTML instead of JSON for {Translation}", translation);
                return [];
            }

            using var doc = JsonDocument.Parse(json);
            var contentArr = doc.RootElement.GetProperty("chapter").GetProperty("content");

            return HelloaoChapterParser.ParseVerses(
                contentArr, translation, reference.Book, reference.Chapter,
                reference.VerseStart, reference.VerseEnd ?? reference.VerseStart);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warning("Free Bible API request for {Ref} timed out", reference);
            return [];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch from free Bible API");
            return [];
        }
    }

    public Task<List<string>> GetAvailableTranslationsAsync()
        => Task.FromResult(new List<string>(FallbackTranslationIds.Keys));
}
