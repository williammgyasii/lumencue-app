using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

/// <summary>
/// One ranked song match. <see cref="Section"/> is the best-matching section and <see cref="Snippet"/>
/// is the single lyric line that best matched the query (for display under the song title).
/// </summary>
public sealed record SongSearchHit(Song Song, SongSection? Section, string Snippet, double Score);

/// <summary>
/// Smart lyric search across the local song library: phrase, token-coverage, prefix and
/// typo-tolerant (fuzzy) matching, ranked. Runs entirely in-memory for real-time as-you-type use.
/// </summary>
public interface ISongSearchService
{
    /// <summary>
    /// Ranks songs for <paramref name="query"/>. An empty query returns the whole library
    /// (alphabetical), so the tab doubles as a song browser.
    /// </summary>
    Task<IReadOnlyList<SongSearchHit>> SearchAsync(string query, int maxResults = 30, CancellationToken cancellationToken = default);

    /// <summary>Marks the in-memory index stale so the next search rebuilds it (after edits/sync).</summary>
    void Invalidate();
}
