using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Topical / paraphrase scripture lookup: given a loose description of a passage ("go into the
/// nations and preach"), returns the verses that best match by meaning and keywords. Backed by a
/// hybrid of semantic embeddings over the cached Bible plus keyword search.
/// </summary>
public interface IScriptureSearchService
{
    /// <summary>True when the semantic index for the given translation is loaded and ready.</summary>
    bool IsIndexReady(string translation);

    /// <summary>
    /// Builds (or loads from disk) the semantic index for a translation. Safe to call repeatedly;
    /// it returns immediately once the index is ready. No-ops gracefully if embeddings are not yet
    /// available, leaving keyword search fully functional.
    /// </summary>
    Task EnsureIndexedAsync(string translation, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Ranks cached verses by relevance to the query phrase.</summary>
    Task<List<ScriptureSearchHit>> SearchAsync(string query, string translation, int maxResults = 12, CancellationToken cancellationToken = default);
}
