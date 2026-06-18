using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Parsing;
using ChurchProjection.Core.Services;
using FuzzySharp;
using Serilog;

namespace ChurchProjection.Infrastructure.Matching;

public class FuzzyAiMatcherService : IAiMatcherService
{
    private const int MinWordsForFuzzy = 3;
    private const int MinFuzzyScore = 55;
    private const int MaxResults = 5;
    // Upper bound on how many individual verse cards a single spoken range may produce, so an
    // accidental "verse one to one hundred" can't flood the suggestions list.
    private const int MaxRangeVerses = 30;
    private const int BodyPreviewLength = 200;

    private readonly IContentLibraryService _contentLibrary;

    // Immutable snapshot swapped on update so reads never take a lock.
    private volatile IReadOnlyList<(string Id, string Text)> _index = [];

    // Selected translation; written from the UI thread, read on the matcher worker. Reference
    // assignment is atomic and volatile guarantees the worker sees the latest value.
    private volatile string _currentTranslation = "BSB";

    // Anchor for spoken verse navigation ("next verse"); updated only from the once-per-utterance
    // segment stream so it is never reset by stale sliding-window text. Always a single verse.
    private volatile ScriptureReference? _navAnchor;

    // Stitches a reference spoken in fragments across pauses (book → chapter → verse). The gap timeout
    // comfortably bridges a preacher's mid-reference pauses without gluing on unrelated later numbers.
    private readonly SpokenReferenceBuilder _referenceBuilder = new(TimeSpan.FromSeconds(30));

    // Written from the UI thread on mode switches; read on the matcher worker. Volatile is enough.
    private volatile bool _includeContentMatches = true;

    public string CurrentTranslation
    {
        get => _currentTranslation;
        set => _currentTranslation = string.IsNullOrWhiteSpace(value) ? "BSB" : value;
    }

    public bool IncludeContentMatches
    {
        get => _includeContentMatches;
        set => _includeContentMatches = value;
    }

    public FuzzyAiMatcherService(IContentLibraryService contentLibrary)
    {
        _contentLibrary = contentLibrary;
    }

    public void UpdateContentLibrary(IEnumerable<string> contentTexts, IEnumerable<string> contentIds)
    {
        var texts = contentTexts.ToList();
        var ids = contentIds.ToList();
        var count = Math.Min(texts.Count, ids.Count);

        var next = new List<(string Id, string Text)>(count);
        for (int i = 0; i < count; i++)
            next.Add((ids[i], texts[i]));

        _index = next;
        Log.Debug("AI matcher index updated with {Count} items", next.Count);
    }

    public async Task<List<AiSuggestion>> MatchAsync(string transcriptChunk, bool scriptureOnly = false, CancellationToken cancellationToken = default)
    {
        var suggestions = new List<AiSuggestion>();

        var refs = ScriptureReferenceParser.ExtractFromSpoken(transcriptChunk);
        if (refs.Count == 0)
            refs = ScriptureReferenceParser.ExtractAll(transcriptChunk);

        foreach (var r in refs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AddScriptureSuggestionsAsync(r, suggestions, cancellationToken).ConfigureAwait(false);
        }

        if (scriptureOnly || !_includeContentMatches || suggestions.Count >= MaxResults) return suggestions;

        AddFuzzySuggestions(transcriptChunk, suggestions, cancellationToken);
        return suggestions;
    }

    public void NoteSpokenSegment(string finalSegmentText)
    {
        if (string.IsNullOrWhiteSpace(finalSegmentText)) return;

        var refs = ScriptureReferenceParser.ExtractFromSpoken(finalSegmentText);
        if (refs.Count == 0) return;

        // The most recently spoken reference becomes the navigation anchor. A whole-chapter
        // mention ("Matthew 5") anchors to verse 1 so "next verse" lands on verse 2.
        var last = refs[^1];
        _navAnchor = new ScriptureReference(last.Book, last.Chapter, last.VerseStart, VerseEnd: null);
    }

    public async Task<List<AiSuggestion>> AccumulateSpokenAsync(string finalSegmentText, CancellationToken cancellationToken = default)
    {
        var reference = _referenceBuilder.Accept(finalSegmentText, DateTimeOffset.UtcNow);
        if (reference is null) return [];

        // Keep the spoken-navigation anchor aligned with what was just (progressively) referenced.
        _navAnchor = new ScriptureReference(reference.Book, reference.Chapter, reference.VerseStart, VerseEnd: null);

        var suggestions = new List<AiSuggestion>();
        await AddScriptureSuggestionsAsync(reference, suggestions, cancellationToken).ConfigureAwait(false);
        return suggestions;
    }

    public async Task<List<AiSuggestion>> NavigateAsync(NavCommand command, CancellationToken cancellationToken = default)
    {
        var anchor = _navAnchor;
        if (anchor is null || command == NavCommand.None) return [];

        var targetVerse = command == NavCommand.NextVerse ? anchor.VerseStart + 1 : anchor.VerseStart - 1;
        if (targetVerse < 1) return [];

        var target = new ScriptureReference(anchor.Book, anchor.Chapter, targetVerse, VerseEnd: null);

        var verses = await _contentLibrary
            .GetOrFetchVersesAsync(target, CurrentTranslation, localOnly: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (verses.Count == 0) return [];

        // Step the anchor forward so a string of "next verse" commands keeps advancing.
        _navAnchor = target;

        var v = verses[0];
        return
        [
            new AiSuggestion(
                ContentId: $"scripture:{v.Book}:{v.Chapter}:{v.VerseStart}",
                Title: v.Reference,
                Body: v.Text,
                Footer: $"{v.Reference} ({v.Translation})",
                Score: 1.0,
                MatchType: "scripture_reference"),
        ];
    }

    private async Task AddScriptureSuggestionsAsync(ScriptureReference r, List<AiSuggestion> suggestions, CancellationToken cancellationToken)
    {
        var isWholeChapter = r.VerseStart == 1 && r.VerseEnd is >= ScriptureReference.WholeChapterSentinel;

        // Resolve against the operator's selected translation. Downloaded translations are an
        // instant local (memory/SQLite) hit; anything not yet cached hydrates the whole chapter in
        // a single call. This runs on the engine's background worker and is cancelled the moment a
        // fresher transcript window arrives, so a slow fetch never stalls the live UI.
        var verses = await _contentLibrary
            .GetOrFetchVersesAsync(r, CurrentTranslation, localOnly: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (verses.Count == 0)
        {
            // A specific verse that doesn't exist in the chapter — a mis-spoken or mis-heard number
            // such as "Isaiah 4:8" when Isaiah 4 has only 6 verses — would otherwise surface nothing,
            // leaving the operator thinking the reference was missed entirely. If the chapter itself
            // exists, fall back to it (labelled as the chapter, never a wrong specific verse) so the
            // recognised book + chapter stays actionable.
            if (!isWholeChapter && r.VerseStart > 0)
                await AddOutOfRangeChapterFallbackAsync(r, suggestions, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (isWholeChapter)
        {
            // A chapter-only mention ("Matthew 5") almost always means verse 1. Surface just that;
            // the operator can pull the rest into the library via "Show Full Chapter" if needed.
            var first = verses[0];
            suggestions.Add(new AiSuggestion(
                ContentId: $"scripture:{first.Book}:{first.Chapter}:{first.VerseStart}",
                Title: first.Reference,
                Body: first.Text,
                Footer: $"{first.Reference} ({first.Translation})",
                Score: 1.0,
                MatchType: "scripture_reference"));
            return;
        }

        // An explicitly spoken verse range ("John 1 verse one to five") is surfaced as one card per
        // verse — John 1:1, John 1:2, … John 1:5 — because the preacher reads them one at a time, so
        // the operator wants to project each individually rather than the whole block at once. The
        // span is shown in full (not capped at MaxResults, which is for fuzzy lyric matches).
        var isExplicitRange = r.VerseEnd.HasValue && r.VerseEnd.Value > r.VerseStart;
        var take = isExplicitRange ? MaxRangeVerses : MaxResults;
        var ordered = isExplicitRange ? verses.OrderBy(v => v.VerseStart).ToList() : verses;

        foreach (var v in ordered.Take(take))
        {
            suggestions.Add(new AiSuggestion(
                ContentId: $"scripture:{v.Book}:{v.Chapter}:{v.VerseStart}",
                Title: v.Reference,
                Body: v.Text,
                Footer: $"{v.Reference} ({v.Translation})",
                Score: 1.0,
                MatchType: "scripture_reference"));
        }
    }

    // Surfaces the chapter when a spoken verse was out of range. The chapter was just hydrated into the
    // cache by the failed verse lookup, so this is an instant local hit (no extra network round-trip).
    private async Task AddOutOfRangeChapterFallbackAsync(ScriptureReference r, List<AiSuggestion> suggestions, CancellationToken cancellationToken)
    {
        var chapterRef = new ScriptureReference(r.Book, r.Chapter, VerseStart: 1, VerseEnd: ScriptureReference.WholeChapterSentinel);
        var chapterVerses = await _contentLibrary
            .GetOrFetchVersesAsync(chapterRef, CurrentTranslation, localOnly: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (chapterVerses.Count == 0) return;

        var first = chapterVerses[0];
        suggestions.Add(new AiSuggestion(
            ContentId: $"scripture:{first.Book}:{first.Chapter}:{first.VerseStart}",
            Title: $"{first.Book} {first.Chapter}",
            Body: first.Text,
            Footer: $"{first.Book} {first.Chapter} ({first.Translation}) — verse {r.VerseStart} not found",
            Score: 0.9,
            MatchType: "scripture_reference"));
    }

    private void AddFuzzySuggestions(string transcriptChunk, List<AiSuggestion> suggestions, CancellationToken cancellationToken)
    {
        var index = _index;
        if (index.Count == 0) return;

        var words = transcriptChunk.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < MinWordsForFuzzy) return;

        cancellationToken.ThrowIfCancellationRequested();

        var scoredItems = index
            .Select(item => (item.Id, item.Text, Score: Fuzz.TokenSetRatio(transcriptChunk, item.Text)))
            .Where(x => x.Score >= MinFuzzyScore)
            .OrderByDescending(x => x.Score)
            .Take(MaxResults)
            .ToList();

        foreach (var match in scoredItems)
        {
            if (suggestions.Any(s => s.ContentId == match.Id)) continue;

            var lines = match.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var title = lines.Length > 0 ? lines[0] : match.Id;
            var body = match.Text.Length > BodyPreviewLength ? match.Text[..BodyPreviewLength] + "..." : match.Text;

            suggestions.Add(new AiSuggestion(
                ContentId: match.Id,
                Title: title,
                Body: body,
                Footer: "",
                Score: match.Score / 100.0,
                MatchType: "fuzzy"));
        }
    }
}
