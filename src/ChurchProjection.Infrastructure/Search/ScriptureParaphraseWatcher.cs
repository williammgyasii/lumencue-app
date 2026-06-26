using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Parsing;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Search;

/// <summary>
/// Conservative paraphrase detector over the topical scripture search. Keeps only strong, semantic
/// matches and de-duplicates recent verses so the "Detected while preaching" lane stays quiet and
/// trustworthy. All matching reuses <see cref="IScriptureSearchService"/> (embeddings over the cached
/// Bible); this type only decides what is confident enough to show.
/// </summary>
public sealed class ScriptureParaphraseWatcher : IScriptureParaphraseWatcher
{
    // Tuned for precision over recall. Semantic cosine for a genuine paraphrase typically lands well
    // above this; loose topical overlap falls below it. Calibrated against live preaching.
    private const double HighConfidenceThreshold = 0.5;
    // Below this many words an utterance is filler ("amen", "yes Lord") — never worth a Bible scan.
    private const int MinWords = 6;
    // A paraphrase maps to a small set of verses, not a topical page of twelve.
    private const int MaxDetectionsPerUtterance = 2;
    // How long a detected verse stays suppressed so the same passage isn't re-added as the preacher
    // lingers on it across consecutive sentences.
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(2);

    private readonly IScriptureSearchService _search;
    private readonly object _seenLock = new();
    private readonly Dictionary<string, DateTimeOffset> _recentlyDetected = new();

    public ScriptureParaphraseWatcher(IScriptureSearchService search)
    {
        _search = search;
    }

    public async Task<IReadOnlyList<ParaphraseDetection>> DetectAsync(string utterance, string translation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(utterance)) return [];
        if (CountWords(utterance) < MinWords) return [];

        // A bare reference ("John 3:16") is the AI Suggestions path's job; skip it here so the same
        // hit isn't shown in two places (and so we don't waste a Bible scan on it).
        if (ScriptureReferenceParser.TryParse(utterance) is not null) return [];

        List<ScriptureSearchHit> hits;
        try
        {
            hits = await _search.SearchAsync(utterance, translation, MaxDetectionsPerUtterance * 4, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never let a lookup failure disrupt the live transcription flow.
            Log.Warning(ex, "Paraphrase detection search failed");
            return [];
        }

        var confident = hits
            .Where(h => h.MatchKind != ScriptureSearchHit.KindKeyword && h.Score >= HighConfidenceThreshold)
            .OrderByDescending(h => h.Score)
            .ToList();

        if (confident.Count == 0) return [];

        var now = DateTimeOffset.UtcNow;
        var results = new List<ParaphraseDetection>();

        lock (_seenLock)
        {
            PruneExpired(now);
            foreach (var hit in confident)
            {
                if (results.Count >= MaxDetectionsPerUtterance) break;

                var key = Key(hit.Passage);
                if (_recentlyDetected.ContainsKey(key)) continue;

                _recentlyDetected[key] = now;
                results.Add(new ParaphraseDetection(hit.Passage, hit.Score));
            }
        }

        return results;
    }

    private void PruneExpired(DateTimeOffset now)
    {
        var stale = _recentlyDetected
            .Where(kv => now - kv.Value > DedupeWindow)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in stale)
            _recentlyDetected.Remove(key);
    }

    private static string Key(ScripturePassage p) => $"{p.Book}|{p.Chapter}|{p.VerseStart}";

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
