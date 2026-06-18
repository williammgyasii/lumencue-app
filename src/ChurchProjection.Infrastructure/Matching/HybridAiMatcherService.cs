using ChurchProjection.Core.Parsing;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Matching;

/// <summary>
/// Combines fast fuzzy/keyword matching with semantic (embedding) similarity. The semantic
/// index is stored as an immutable snapshot that is swapped atomically, so live matching reads
/// it lock-free and embedding inference never holds a lock.
/// </summary>
public class HybridAiMatcherService : IAiMatcherService
{
    private const float MinSemanticScore = 0.35f;
    private const int MaxSemanticResults = 3;
    private const int MaxEmbedTextLength = 400;
    private const int BodyPreviewLength = 200;

    private readonly FuzzyAiMatcherService _fuzzy;
    private readonly SemanticEmbeddingService _embeddings;

    private sealed record IndexedItem(string Id, string Text, float[] Embedding);

    // Swapped atomically on rebuild; readers take a local copy and never lock.
    private volatile IReadOnlyList<IndexedItem> _semanticIndex = [];

    public HybridAiMatcherService(FuzzyAiMatcherService fuzzy, SemanticEmbeddingService embeddings)
    {
        _fuzzy = fuzzy;
        _embeddings = embeddings;
    }

    public string CurrentTranslation
    {
        get => _fuzzy.CurrentTranslation;
        set => _fuzzy.CurrentTranslation = value;
    }

    public bool IncludeContentMatches
    {
        get => _fuzzy.IncludeContentMatches;
        set => _fuzzy.IncludeContentMatches = value;
    }

    public void NoteSpokenSegment(string finalSegmentText) => _fuzzy.NoteSpokenSegment(finalSegmentText);

    public Task<List<AiSuggestion>> NavigateAsync(NavCommand command, CancellationToken cancellationToken = default)
        => _fuzzy.NavigateAsync(command, cancellationToken);

    public Task<List<AiSuggestion>> AccumulateSpokenAsync(string finalSegmentText, CancellationToken cancellationToken = default)
        => _fuzzy.AccumulateSpokenAsync(finalSegmentText, cancellationToken);

    public void UpdateContentLibrary(IEnumerable<string> contentTexts, IEnumerable<string> contentIds)
    {
        _fuzzy.UpdateContentLibrary(contentTexts, contentIds);

        if (!_embeddings.IsReady) return;

        var texts = contentTexts.ToList();
        var ids = contentIds.ToList();

        // Embedding the whole library is CPU heavy; build off-thread and swap the snapshot in.
        _ = Task.Run(() =>
        {
            try
            {
                var count = Math.Min(texts.Count, ids.Count);
                var next = new List<IndexedItem>(count);

                for (int i = 0; i < count; i++)
                {
                    var truncated = texts[i].Length > MaxEmbedTextLength ? texts[i][..MaxEmbedTextLength] : texts[i];
                    var emb = _embeddings.Embed(truncated);
                    if (emb is not null)
                        next.Add(new IndexedItem(ids[i], texts[i], emb));
                }

                _semanticIndex = next;
                Log.Debug("Semantic index rebuilt with {Count} items", next.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to rebuild semantic index");
            }
        });
    }

    public async Task<List<AiSuggestion>> MatchAsync(string transcriptChunk, bool scriptureOnly = false, CancellationToken cancellationToken = default)
    {
        var suggestions = await _fuzzy.MatchAsync(transcriptChunk, scriptureOnly, cancellationToken).ConfigureAwait(false);

        // Interim (live partial) matches stay scripture-only: skip the costly embedding inference so
        // spoken verses surface instantly instead of waiting on the semantic pass every 250ms.
        var index = _semanticIndex;
        if (scriptureOnly || !_fuzzy.IncludeContentMatches || !_embeddings.IsReady || index.Count == 0)
            return suggestions;

        cancellationToken.ThrowIfCancellationRequested();

        // Inference runs here on the background matcher thread, never under a lock.
        var queryEmbedding = _embeddings.Embed(transcriptChunk);
        if (queryEmbedding is null)
            return suggestions;

        var existingIds = new HashSet<string>(suggestions.Select(s => s.ContentId));

        var semanticMatches = index
            .Select(item => (item.Id, item.Text, Score: SemanticEmbeddingService.CosineSimilarity(queryEmbedding, item.Embedding)))
            .Where(x => x.Score >= MinSemanticScore && !existingIds.Contains(x.Id))
            .OrderByDescending(x => x.Score)
            .Take(MaxSemanticResults)
            .ToList();

        foreach (var match in semanticMatches)
        {
            var lines = match.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var title = lines.Length > 0 ? lines[0] : match.Id;
            var body = match.Text.Length > BodyPreviewLength ? match.Text[..BodyPreviewLength] + "..." : match.Text;

            suggestions.Add(new AiSuggestion(
                ContentId: match.Id,
                Title: title,
                Body: body,
                Footer: "",
                Score: match.Score,
                MatchType: "semantic"));
        }

        return suggestions;
    }
}
