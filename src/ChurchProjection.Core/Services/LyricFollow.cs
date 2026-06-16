namespace ChurchProjection.Core.Services;

/// <summary>How aggressively the lyric-follow feature acts on what it hears.</summary>
public enum LyricFollowMode
{
    /// <summary>Disabled.</summary>
    Off,

    /// <summary>Highlights/tees-up the slide it believes is being sung; the operator still sends it.</summary>
    Assist,

    /// <summary>Advances the live output automatically (reserved for a future, opt-in step).</summary>
    Auto
}

/// <summary>The outcome of scoring a transcript window against a song's slides.</summary>
/// <param name="BestIndex">Index of the highest-scoring slide, or -1 when nothing was scored.</param>
/// <param name="BestScore">Score of the best slide.</param>
/// <param name="SecondScore">Score of the runner-up (used as an ambiguity guard).</param>
/// <param name="Scores">Per-slide scores, parallel to the input slide list.</param>
public sealed record FollowEvaluation(int BestIndex, double BestScore, double SecondScore, IReadOnlyList<double> Scores);

/// <summary>
/// Pure, stateless lyric matcher. Given the recognised transcript window and the tokenised lyric
/// slides of the <em>currently open</em> song, it scores each slide so the operator UI can decide
/// (with its own debounce/cooldown policy) whether to suggest a move.
///
/// Matching favours the longest contiguous run of words shared between what was heard and a slide —
/// a phrase match is far more discriminating than loose word overlap, which is what keeps a verse
/// from being confused with a chorus that reuses the same vocabulary. Words that occur on many of
/// the song's own slides are down-weighted so distinctive lines carry the decision.
/// </summary>
public static class LyricFollow
{
    /// <summary>Lower-cases, strips punctuation and splits text into comparable word tokens.</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>
    /// Scores <paramref name="window"/> (recent recognised words) against each slide in
    /// <paramref name="slides"/>. Returns -1 best index when there is nothing meaningful to score.
    /// </summary>
    public static FollowEvaluation Evaluate(IReadOnlyList<IReadOnlyList<string>> slides, IReadOnlyList<string> window)
    {
        var scores = new double[slides.Count];
        if (slides.Count == 0 || window.Count == 0)
            return new FollowEvaluation(-1, 0, 0, scores);

        var weights = BuildWeights(slides);

        for (var s = 0; s < slides.Count; s++)
            scores[s] = ScoreSlide(slides[s], window, weights);

        var bestIndex = -1;
        double best = 0, second = 0;
        for (var s = 0; s < scores.Length; s++)
        {
            if (scores[s] > best)
            {
                second = best;
                best = scores[s];
                bestIndex = s;
            }
            else if (scores[s] > second)
            {
                second = scores[s];
            }
        }

        // Nothing matched at all: report no candidate so the caller holds position.
        if (best <= 0) bestIndex = -1;
        return new FollowEvaluation(bestIndex, best, second, scores);
    }

    /// <summary>Inverse-document-frequency style weighting across the song's own slides.</summary>
    private static Dictionary<string, double> BuildWeights(IReadOnlyList<IReadOnlyList<string>> slides)
    {
        var df = new Dictionary<string, int>();
        foreach (var slide in slides)
            foreach (var word in slide.Distinct())
                df[word] = df.TryGetValue(word, out var c) ? c + 1 : 1;

        var weights = new Dictionary<string, double>(df.Count);
        foreach (var (word, count) in df)
            weights[word] = 1.0 / (1.0 + Math.Log(count));
        return weights;
    }

    private static double Weight(Dictionary<string, double> weights, string word)
        => weights.TryGetValue(word, out var w) ? w : 1.0;

    private static double ScoreSlide(IReadOnlyList<string> slide, IReadOnlyList<string> window, Dictionary<string, double> weights)
    {
        if (slide.Count == 0) return 0;

        // Longest contiguous matched run (weighted) — the dominant signal.
        double bestRun = 0;
        for (var p = 0; p < slide.Count; p++)
        {
            for (var i = 0; i < window.Count; i++)
            {
                if (slide[p] != window[i]) continue;
                double run = 0;
                int a = p, b = i;
                while (a < slide.Count && b < window.Count && slide[a] == window[b])
                {
                    run += Weight(weights, slide[a]);
                    a++;
                    b++;
                }
                if (run > bestRun) bestRun = run;
            }
        }

        // Loose overlap of distinct heard words present anywhere in the slide — a weak tie-breaker.
        double overlap = 0;
        var slideSet = new HashSet<string>(slide);
        foreach (var word in window.Distinct())
            if (slideSet.Contains(word))
                overlap += Weight(weights, word);

        return bestRun + 0.25 * overlap;
    }
}
