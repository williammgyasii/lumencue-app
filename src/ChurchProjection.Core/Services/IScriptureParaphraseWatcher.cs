using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Watches finalized speech for paraphrased scripture (the preacher loosely restating a passage
/// rather than citing a reference) and returns the verses he is most likely echoing. Conservative by
/// design: it surfaces only confident, semantic matches so the operator's "Detected while preaching"
/// lane stays quiet and trustworthy instead of reacting to every sentence.
/// </summary>
public interface IScriptureParaphraseWatcher
{
    /// <summary>
    /// Inspects a single finalized utterance and returns any confident paraphrase matches (often
    /// none). Repeats of a recently-detected verse are de-duplicated. Never throws for live-flow
    /// safety; lookup failures yield an empty result.
    /// </summary>
    Task<IReadOnlyList<ParaphraseDetection>> DetectAsync(string utterance, string translation, CancellationToken cancellationToken = default);
}

/// <summary>A verse the preacher is likely paraphrasing, with the match confidence (0..1).</summary>
public record ParaphraseDetection(ScripturePassage Passage, double Score);
