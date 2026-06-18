namespace ChurchProjection.Core.Services;

/// <summary>
/// Owns the real-time transcript-to-suggestion pipeline. Callers push the latest
/// transcript window and observe ranked suggestions; all matching runs off the UI
/// thread with latest-wins semantics so stale windows never block fresh ones.
/// </summary>
public interface ISuggestionEngine : IDisposable
{
    /// <summary>Ranked suggestions for the most recently processed transcript window.</summary>
    IObservable<IReadOnlyList<AiSuggestion>> Suggestions { get; }

    /// <summary>
    /// Submit the newest transcript window for matching. Non-blocking. If a window is
    /// already being processed it is superseded by this one (older queued work is dropped).
    /// </summary>
    void Push(string transcriptWindow);

    /// <summary>
    /// Submit the latest live (interim) transcript window. Matched scripture-only and on its own
    /// latest-wins slot, so verses spoken in the in-progress sentence surface immediately without the
    /// cost of the fuzzy/semantic passes and without dropping the full finalised-window match.
    /// </summary>
    void PushInterim(string transcriptWindow);

    /// <summary>
    /// Submit a single final utterance for command handling. Spoken navigation ("next verse")
    /// emits a fresh suggestion; otherwise the utterance updates the scripture navigation anchor.
    /// Should be called once per final segment, not per sliding window.
    /// </summary>
    void HandleSegment(string finalSegmentText);
}
