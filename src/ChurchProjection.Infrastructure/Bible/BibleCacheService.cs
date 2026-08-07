using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using ChurchProjection.Core.Bible;
using ChurchProjection.Infrastructure.Data;
using Dapper;
using Serilog;

namespace ChurchProjection.Infrastructure.Bible;

public class BibleCacheService
{
    private const int TotalBibleChapters = 1189;
    private const int TotalBibleBooks = 66;
    private const int ProgressBookInterval = 10;
    private const int ProgressChapterInterval = 50;
    private const int ApiBibleConcurrency = 4;

    private readonly DatabaseService _db;
    private readonly HttpClient _http;
    private readonly ScriptureRepository _repo;
    private readonly ApiBibleClient? _apiBible;
    private readonly ConcurrentDictionary<string, bool> _activeDownloads = new();
    private readonly SemaphoreSlim _dbWriteLock;

    private ConcurrentDictionary<string, string> _freeApiTranslations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The curated set of translations offered in the UI, in display order. KJV/BSB are public
    /// domain (served free); the rest are copyrighted and resolved through the API.Bible Pro plan.
    /// </summary>
    private static readonly (string Code, string Name)[] CuratedTranslations =
    [
        ("KJV", "King James Version"),
        ("NIV", "New International Version"),
        ("NKJV", "New King James Version"),
        ("NLT", "New Living Translation"),
        ("MSG", "The Message"),
        ("AMP", "Amplified Bible"),
        ("CSB", "Christian Standard Bible"),
    ];

    /// <summary>
    /// Custom translations we host ourselves as static JSON (not available from the public or premium
    /// Bible APIs). The app downloads each file once and caches it into local SQLite, exactly like the
    /// other translations, after which it works fully offline. Add a new hosted Bible by importing it
    /// (tools/BibleImporter) and adding a row here.
    /// </summary>
    private static readonly (string Code, string Name, string Url)[] CustomTranslations =
    [
        ("TPT", "The Passion Translation",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/TPT.json"),
        ("TLB", "The Living Bible",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/TLB.json"),
        ("AMPC", "Amplified Bible, Classic Edition",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/AMPC.json"),
        ("ESV", "English Standard Version",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/ESV.json"),
        ("GNT", "Good News Translation",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/GNT.json"),
        ("ASV", "American Standard Version",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/ASV.json"),
    ];

    private static (string Code, string Name, string Url)? FindCustom(string translation) =>
        CustomTranslations
            .Cast<(string Code, string Name, string Url)?>()
            .FirstOrDefault(c => string.Equals(c!.Value.Code, translation, StringComparison.OrdinalIgnoreCase));

    private readonly BehaviorSubject<string> _statusMessage = new("");
    public IObservable<string> StatusMessage => _statusMessage.AsObservable();

    public BibleCacheService(DatabaseService db, ScriptureRepository repo, ApiBibleClient? apiBible = null)
    {
        _db = db;
        _repo = repo;
        _apiBible = apiBible;
        _dbWriteLock = db.WriteLock;
        _http = BibleHttpClients.Helloao;
    }

    public async Task<List<(string Id, string Name)>> LoadAvailableTranslationsAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("available_translations.json");
            using var doc = JsonDocument.Parse(json);
            var translations = doc.RootElement.GetProperty("translations");

            var newMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Keep the full free-API map so public-domain picks (KJV/BSB) still route to the free
            // source for fast bulk caching; the UI only surfaces the curated whitelist below.
            foreach (var t in translations.EnumerateArray())
            {
                var id = t.GetProperty("id").GetString() ?? "";
                var lang = t.TryGetProperty("language", out var langEl) ? langEl.GetString() : "";
                if (lang != "eng" || string.IsNullOrEmpty(id)) continue;

                var shortName = t.TryGetProperty("shortName", out var shortEl)
                    ? shortEl.GetString() ?? id
                    : id;

                newMap[shortName] = id;
            }

            _freeApiTranslations = newMap;
            Log.Information("Loaded {Count} English translations from bible.helloao.org; offering {Curated} curated picks",
                newMap.Count, CuratedTranslations.Length);
            return OfferedTranslations();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load available translations, using defaults");
            _freeApiTranslations = new ConcurrentDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BSB"] = "BSB",
                    ["KJV"] = "eng_kjv",
                });
            return OfferedTranslations();
        }
    }

    /// <summary>The translations surfaced in the UI: the curated API-backed picks plus our own hosted
    /// custom translations.</summary>
    private static List<(string Id, string Name)> OfferedTranslations() =>
        CuratedTranslations.Select(c => (c.Code, c.Name))
            .Concat(CustomTranslations.Select(c => (c.Code, c.Name)))
            .ToList();

    public bool CanBulkCache(string translation) =>
        _freeApiTranslations.ContainsKey(translation) || FindCustom(translation) is not null;

    public string? ResolveApiId(string shortName) =>
        _freeApiTranslations.TryGetValue(shortName, out var id) ? id : null;

    public async Task<bool> IsTranslationCachedAsync(string translation)
    {
        await using var conn = _db.GetConnection();
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT is_complete FROM bible_cache_status WHERE translation = @translation",
            new { translation });
        return row?.is_complete == 1L;
    }

    public async Task EnsureTranslationCachedAsync(string translation, CancellationToken ct = default)
    {
        if (await IsTranslationCachedAsync(translation)) return;

        if (!_activeDownloads.TryAdd(translation, true)) return;

        try
        {
            var custom = FindCustom(translation);
            if (custom is not null)
            {
                // Hosted custom translation (e.g. TPT): download our static JSON once and cache it.
                await DownloadCustomTranslationAsync(custom.Value.Code, custom.Value.Url, ct);
                return;
            }

            var apiId = ResolveApiId(translation);
            if (apiId is not null)
            {
                // Public-domain: pull the whole Bible in one /complete.json call.
                await DownloadCompleteTranslationAsync(translation, apiId, ct);
            }
            else if (_apiBible is not null && _apiBible.Supports(translation))
            {
                // Copyrighted (API.Bible): walk every chapter once and cache it locally.
                await DownloadViaApiBibleAsync(translation, ct);
            }
            else
            {
                Log.Information("Translation {T} has no downloadable source -- caching on demand", translation);
            }
        }
        finally
        {
            _activeDownloads.TryRemove(translation, out _);
        }
    }

    /// <summary>Downloads a hosted custom translation (one static JSON file) and bulk-inserts it into
    /// local SQLite, mirroring the public-domain /complete.json path. After this the translation is
    /// fully local and never hits the network again.</summary>
    private async Task DownloadCustomTranslationAsync(string translation, string url, CancellationToken ct)
    {
        Log.Information("Downloading custom translation {Translation} from {Url}", translation, url);
        _statusMessage.OnNext($"Downloading {translation}...");

        try
        {
            var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            var file = CustomBibleFile.FromJson(json);

            var passages = file.Verses
                .Select(v => new Core.Models.Content.ScripturePassage
                {
                    Translation = translation,
                    Book = v.Book,
                    Chapter = v.Chapter,
                    VerseStart = v.Verse,
                    VerseEnd = null,
                    Text = v.Text,
                })
                .ToList();

            var chapterCount = file.Verses.Select(v => (v.Book, v.Chapter)).Distinct().Count();
            await MarkCacheStartedAsync(translation, chapterCount, ct).ConfigureAwait(false);

            const int batchSize = 2000;
            for (var i = 0; i < passages.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                await _repo.BulkInsertAsync(passages.GetRange(i, Math.Min(batchSize, passages.Count - i))).ConfigureAwait(false);
                _statusMessage.OnNext($"Downloading {translation}... {Math.Min(i + batchSize, passages.Count):N0}/{passages.Count:N0} verses");
            }

            await MarkCacheCompleteAsync(translation, chapterCount, ct).ConfigureAwait(false);
            Log.Information("Custom translation cached: {Translation} ({Verses} verses)", translation, passages.Count);
            _statusMessage.OnNext($"{translation} cached ({passages.Count:N0} verses)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to download custom translation {Translation}", translation);
            _statusMessage.OnNext($"Cache failed for {translation}");
        }
    }

    private async Task DownloadViaApiBibleAsync(string translation, CancellationToken ct)
    {
        var chapterIds = await _apiBible!.GetChapterIdsAsync(translation, ct).ConfigureAwait(false);
        if (chapterIds.Count == 0)
        {
            Log.Warning("No chapters returned for {Translation}; skipping bulk download", translation);
            return;
        }

        Log.Information("Downloading entire Bible: {Translation} via API.Bible ({Count} chapters)", translation, chapterIds.Count);
        await MarkCacheStartedAsync(translation, chapterIds.Count, ct).ConfigureAwait(false);
        _statusMessage.OnNext($"Downloading {translation}...");

        var done = 0;
        var verses = 0;
        using var sem = new SemaphoreSlim(ApiBibleConcurrency);

        var tasks = chapterIds.Select(async chapterId =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var parts = chapterId.Split('.');
                var book = BibleBooks.GetName(parts[0]);
                var chapter = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;

                // Resume support: skip chapters already cached from a previous run.
                if (await _repo.CountVersesInChapterAsync(book, chapter, translation).ConfigureAwait(false) == 0)
                {
                    var passages = await _apiBible.FetchChapterByIdAsync(translation, chapterId, ct).ConfigureAwait(false);
                    if (passages.Count > 0)
                    {
                        await _repo.BulkInsertAsync(passages).ConfigureAwait(false);
                        Interlocked.Add(ref verses, passages.Count);
                    }
                }

                var completed = Interlocked.Increment(ref done);
                if (completed % ProgressChapterInterval == 0)
                    _statusMessage.OnNext($"Downloading {translation}... {completed}/{chapterIds.Count} chapters");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Failed to cache chapter {Chapter} for {Translation}", chapterId, translation);
                Interlocked.Increment(ref done);
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        await MarkCacheCompleteAsync(translation, chapterIds.Count, ct).ConfigureAwait(false);
        Log.Information("Bible cached: {Translation} ({Verses} verses)", translation, verses);
        _statusMessage.OnNext($"{translation} cached ({verses:N0} verses)");
    }

    private async Task MarkCacheStartedAsync(string translation, int totalChapters, CancellationToken ct)
    {
        await _dbWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                """
                INSERT OR REPLACE INTO bible_cache_status (translation, total_chapters, cached_chapters, is_complete, started_at)
                VALUES (@translation, @totalChapters, 0, 0, datetime('now'))
                """,
                new { translation, totalChapters });
        }
        finally { _dbWriteLock.Release(); }
    }

    private async Task MarkCacheCompleteAsync(string translation, int totalChapters, CancellationToken ct)
    {
        await _dbWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                "UPDATE bible_cache_status SET cached_chapters = @totalChapters, is_complete = 1, completed_at = datetime('now') WHERE translation = @translation",
                new { translation, totalChapters });
        }
        finally { _dbWriteLock.Release(); }
    }

    private async Task DownloadCompleteTranslationAsync(string translation, string apiId, CancellationToken ct)
    {
        await _dbWriteLock.WaitAsync(ct);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                """
                INSERT OR REPLACE INTO bible_cache_status (translation, total_chapters, cached_chapters, is_complete, started_at)
                VALUES (@translation, @TotalChapters, 0, 0, datetime('now'))
                """,
                new { translation, TotalChapters = TotalBibleChapters });
        }
        finally { _dbWriteLock.Release(); }

        Log.Information("Downloading entire Bible: {Translation} via /api/{ApiId}/complete.json", translation, apiId);
        _statusMessage.OnNext($"Downloading {translation}...");

        try
        {
            var json = await _http.GetStringAsync($"{apiId}/complete.json", ct);
            using var doc = JsonDocument.Parse(json);
            var books = doc.RootElement.GetProperty("books");

            int totalVerses = 0;
            int bookCount = 0;

            foreach (var bookEl in books.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var bookId = bookEl.GetProperty("id").GetString() ?? "";
                var fallbackName = bookEl.TryGetProperty("commonName", out var cn) ? cn.GetString() : bookId;
                var bookName = BibleBooks.GetName(bookId, fallbackName);

                if (!bookEl.TryGetProperty("chapters", out var chapters)) continue;

                foreach (var chapterEl in chapters.EnumerateArray())
                {
                    if (!chapterEl.TryGetProperty("chapter", out var chData)) continue;
                    if (!chData.TryGetProperty("number", out var chNumEl)) continue;
                    var chapterNum = chNumEl.GetInt32();

                    if (!chData.TryGetProperty("content", out var contentArr)) continue;

                    var passages = HelloaoChapterParser.ParseVerses(contentArr, translation, bookName, chapterNum);
                    if (passages.Count > 0)
                    {
                        await _repo.BulkInsertAsync(passages);
                        totalVerses += passages.Count;
                    }
                }

                bookCount++;
                if (bookCount % ProgressBookInterval == 0)
                    _statusMessage.OnNext($"Downloading {translation}... {bookCount}/{TotalBibleBooks} books");
            }

            await _dbWriteLock.WaitAsync(ct);
            try
            {
                await using var conn = _db.GetConnection();
                await conn.ExecuteAsync(
                    "UPDATE bible_cache_status SET cached_chapters = @TotalChapters, is_complete = 1, completed_at = datetime('now') WHERE translation = @translation",
                    new { translation, TotalChapters = TotalBibleChapters });
            }
            finally { _dbWriteLock.Release(); }

            Log.Information("Bible cached: {Translation} ({Verses} verses)", translation, totalVerses);
            _statusMessage.OnNext($"{translation} cached ({totalVerses:N0} verses)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to download complete Bible for {Translation}", translation);
            _statusMessage.OnNext($"Cache failed for {translation}");
        }
    }
}
