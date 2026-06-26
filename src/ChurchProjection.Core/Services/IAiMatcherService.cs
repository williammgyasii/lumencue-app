using ChurchProjection.Core.Parsing;

namespace ChurchProjection.Core.Services;

public interface IAiMatcherService
{
    /// <summary>
    /// Translation the live matcher resolves spoken scripture references against. Kept in sync with
    /// the operator's selected translation so suggestions match what will be projected and hit the
    /// downloaded local cache instead of the default fallback.
    /// </summary>
    string CurrentTranslation { get; set; }

    /// <summary>
    /// When false, live matching surfaces scripture references only and skips song/library fuzzy and
    /// semantic matches. Set false in Bible mode so the suggestions panel doesn't mix songs into a
    /// scripture-driven service.
    /// </summary>
    bool IncludeContentMatches { get; set; }

    /// <summary>
    /// Matches a transcript window. When <paramref name="scriptureOnly"/> is true, only the cheap
    /// scripture-reference resolution runs — the expensive fuzzy and semantic-embedding passes are
    /// skipped. Used for the high-frequency interim (live partial) stream so spoken verses surface
    /// instantly, while the full match (including content matches) runs on finalised utterances.
    /// </summary>
    Task<List<AiSuggestion>> MatchAsync(string transcriptChunk, bool scriptureOnly = false, CancellationToken cancellationToken = default);
    void UpdateContentLibrary(IEnumerable<string> contentTexts, IEnumerable<string> contentIds);

    /// <summary>
    /// Records the scripture reference anchor from a single final utterance so that subsequent
    /// spoken navigation ("next verse") moves relative to what the speaker just referenced. Fed from
    /// the once-per-utterance segment stream, not the sliding window, so the anchor is not reset by
    /// stale window text.
    /// </summary>
    void NoteSpokenSegment(string finalSegmentText);

    /// <summary>
    /// Resolves the verse adjacent to the current anchor in the given direction and advances the
    /// anchor so consecutive commands keep stepping. Returns an empty list when there is no anchor
    /// yet or the target verse cannot be resolved.
    /// </summary>
    Task<List<AiSuggestion>> NavigateAsync(NavCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Feeds one final utterance into a stateful builder that assembles a scripture reference uttered
    /// in fragments across pauses (e.g. "Matthew" … "chapter two" … "verse three"). Returns scripture
    /// suggestions when the pending reference gains new, showable information (book+chapter, then a
    /// refined verse); otherwise an empty list. Fed from the per-utterance segment stream.
    /// </summary>
    Task<List<AiSuggestion>> AccumulateSpokenAsync(string finalSegmentText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fires when a spoken reference parsed to a real book + chapter but resolved to no verses at all
    /// (not even the chapter exists) — i.e. the preacher named a passage that isn't in the Bible. The
    /// UI surfaces this as a transient "doesn't exist" toast. Already de-duplicated so a reference
    /// repeated across sliding transcript windows fires at most once per short window.
    /// </summary>
    IObservable<ReferenceNotFound> ReferenceNotFound { get; }
}

/// <summary>A scripture reference that was requested but does not exist in the Bible.</summary>
/// <param name="Reference">Human-readable reference, e.g. "John 99:5".</param>
public record ReferenceNotFound(string Reference);

public record AiSuggestion(
    string ContentId,
    string Title,
    string Body,
    string Footer,
    double Score,
    string MatchType);
