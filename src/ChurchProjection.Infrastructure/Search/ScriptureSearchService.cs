using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Data;
using ChurchProjection.Infrastructure.Matching;
using Serilog;

namespace ChurchProjection.Infrastructure.Search;

/// <summary>
/// Hybrid topical scripture search: semantic similarity (MiniLM embeddings over the cached Bible)
/// merged with keyword matching. The per-translation embedding index is built once and cached to
/// disk, so subsequent runs load instantly. Keyword search always works even before the index is
/// built or if embeddings are unavailable.
/// </summary>
public sealed class ScriptureSearchService : IScriptureSearchService
{
    private const int FileMagic = 0x42494256; // "BIBV"
    private const int FileVersion = 1;
    private const int EmbeddingDim = 384;
    private const double MinSemanticScore = ScriptureSearchRanker.MinSemanticScore;

    private readonly ScriptureRepository _repo;
    private readonly SemanticEmbeddingService _embeddings;
    private readonly string _cacheDir;
    private readonly SemaphoreSlim _buildLock = new(1, 1);

    private volatile SemanticIndex? _index;

    private sealed class SemanticIndex
    {
        public required string Translation { get; init; }
        public required ScripturePassage[] Verses { get; init; }
        public required float[][] Embeddings { get; init; }
    }

    public ScriptureSearchService(ScriptureRepository repo, SemanticEmbeddingService embeddings)
    {
        _repo = repo;
        _embeddings = embeddings;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChurchProjection", "embeddings");
    }

    public bool IsIndexReady(string translation) =>
        string.Equals(_index?.Translation, translation, StringComparison.OrdinalIgnoreCase);

    public async Task EnsureIndexedAsync(string translation, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        // Already indexed and the cached Bible hasn't grown since — nothing to do.
        if (IsIndexReady(translation) &&
            _index!.Verses.Length == await _repo.CountVersesAsync(translation).ConfigureAwait(false))
            return;

        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var acquired = true;
        try
        {
            if (IsIndexReady(translation) &&
                _index!.Verses.Length == await _repo.CountVersesAsync(translation).ConfigureAwait(false))
                return;

            var verses = await _repo.GetAllVersesAsync(translation).ConfigureAwait(false);
            if (verses.Count == 0)
            {
                Log.Information("Topical search: no cached verses for {Translation} yet", translation);
                return;
            }

            // Try the on-disk embedding cache first.
            var loaded = TryLoadFromDisk(translation, verses);
            if (loaded is not null)
            {
                _index = loaded;
                Log.Information("Topical search: loaded cached index for {Translation} ({Count} verses)", translation, verses.Count);
                return;
            }

            if (!_embeddings.IsReady)
            {
                Log.Information("Topical search: embeddings not ready; keyword-only for {Translation}", translation);
                return;
            }

            progress?.Report($"Indexing {translation} for topical search...");
            var embeddings = new float[verses.Count][];
            for (int i = 0; i < verses.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                embeddings[i] = _embeddings.Embed(verses[i].Text) ?? new float[EmbeddingDim];
                if (i % 1000 == 0 && i > 0)
                    progress?.Report($"Indexing {translation}: {i:N0}/{verses.Count:N0} verses");
            }

            var index = new SemanticIndex
            {
                Translation = translation,
                Verses = [.. verses],
                Embeddings = embeddings,
            };
            _index = index;

            TrySaveToDisk(index);
            progress?.Report($"Topical search ready for {translation}");
            Log.Information("Topical search: built index for {Translation} ({Count} verses)", translation, verses.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to build topical search index for {Translation}", translation);
        }
        finally
        {
            if (acquired)
                _buildLock.Release();
        }
    }

    public async Task<List<ScriptureSearchHit>> SearchAsync(string query, string translation, int maxResults = 12, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // Keep the semantic index warm/fresh in the background (self-guards if already current).
        _ = EnsureIndexedAsync(translation);

        var tokens = ScriptureSearchRanker.Tokenize(query);

        // Keyword half (always available). Ranker re-scores with word boundaries.
        var keywordHits = tokens.Count > 0
            ? await _repo.SearchByKeywordsAsync(tokens, translation, maxResults * 3).ConfigureAwait(false)
            : [];

        var semanticHits = new List<(ScripturePassage Passage, double Cosine)>();
        var index = _index;
        if (index is not null && string.Equals(index.Translation, translation, StringComparison.OrdinalIgnoreCase) && _embeddings.IsReady)
        {
            var queryEmbedding = _embeddings.Embed(query);
            if (queryEmbedding is not null)
            {
                var scored = new List<(int Idx, double Score)>(index.Verses.Length);
                for (int i = 0; i < index.Verses.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double sim = SemanticEmbeddingService.CosineSimilarity(queryEmbedding, index.Embeddings[i]);
                    if (sim >= MinSemanticScore)
                        scored.Add((i, sim));
                }

                foreach (var (idx, sim) in scored.OrderByDescending(s => s.Score).Take(maxResults * 3))
                    semanticHits.Add((index.Verses[idx], sim));
            }
        }

        return ScriptureSearchRanker.Rank(query, keywordHits, semanticHits, maxResults);
    }

    private string CacheFilePath(string translation) =>
        Path.Combine(_cacheDir, $"bible-{translation.ToLowerInvariant()}.vec");

    private SemanticIndex? TryLoadFromDisk(string translation, List<ScripturePassage> verses)
    {
        var path = CacheFilePath(translation);
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != FileMagic || reader.ReadInt32() != FileVersion) return null;
            var dim = reader.ReadInt32();
            var count = reader.ReadInt32();

            // Stale if the cached Bible changed since the index was built.
            if (dim != EmbeddingDim || count != verses.Count) return null;

            var embeddings = new float[count][];
            var buffer = new byte[dim * sizeof(float)];
            for (int i = 0; i < count; i++)
            {
                var vec = new float[dim];
                if (reader.Read(buffer, 0, buffer.Length) != buffer.Length) return null;
                Buffer.BlockCopy(buffer, 0, vec, 0, buffer.Length);
                embeddings[i] = vec;
            }

            return new SemanticIndex
            {
                Translation = translation,
                Verses = [.. verses],
                Embeddings = embeddings,
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load topical index cache for {Translation}; will rebuild", translation);
            return null;
        }
    }

    private void TrySaveToDisk(SemanticIndex index)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var path = CacheFilePath(index.Translation);
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);

            writer.Write(FileMagic);
            writer.Write(FileVersion);
            writer.Write(EmbeddingDim);
            writer.Write(index.Embeddings.Length);

            var buffer = new byte[EmbeddingDim * sizeof(float)];
            foreach (var vec in index.Embeddings)
            {
                Buffer.BlockCopy(vec, 0, buffer, 0, buffer.Length);
                writer.Write(buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist topical index cache for {Translation}", index.Translation);
        }
    }
}
