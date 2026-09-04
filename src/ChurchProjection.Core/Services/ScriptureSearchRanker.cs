using System.Text.RegularExpressions;
using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Orders topical Find Scripture hits. Phrase and word-boundary keyword matches sit above
/// semantic-only cosine hits. Pure merge — no SQLite, no embeddings.
/// </summary>
public static class ScriptureSearchRanker
{
    public const double MinSemanticScore = 0.40;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "into", "on", "at", "for", "with", "that",
        "this", "is", "are", "was", "were", "be", "it", "its", "as", "by", "from", "you", "your",
        "we", "us", "our", "he", "she", "they", "them", "his", "her", "their", "i", "me", "my",
        "about", "where", "when", "what", "who", "how", "can", "will", "would", "should", "shall",
        "go", "get", "got", "say", "says", "said", "talk", "talks", "scripture", "verse", "verses",
        "passage", "part", "find", "show", "bring", "give", "please", "thing", "things", "some",
    };

    public static List<string> Tokenize(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var words = query.Split([' ', ',', '.', ';', ':', '!', '?', '"', '\'', '(', ')', '-', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries);

        var result = new List<string>();
        foreach (var w in words)
        {
            var token = w.Trim().ToLowerInvariant();
            if (token.Length < 3 || StopWords.Contains(token)) continue;
            if (!result.Contains(token)) result.Add(token);
            if (result.Count >= 8) break;
        }
        return result;
    }

    public static bool HasKeyword(string verseText, string token)
    {
        if (string.IsNullOrWhiteSpace(verseText) || string.IsNullOrWhiteSpace(token))
            return false;
        return Regex.IsMatch(
            verseText,
            $@"\b{Regex.Escape(token)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool HasPhrase(string verseText, string query)
    {
        var needle = Normalize(query);
        if (needle.Length == 0) return false;
        return Normalize(verseText).Contains(needle, StringComparison.Ordinal);
    }

    public static List<ScriptureSearchHit> Rank(
        string query,
        IReadOnlyList<ScripturePassage> keywordCandidates,
        IReadOnlyList<(ScripturePassage Passage, double Cosine)> semanticCandidates,
        int maxResults = 12)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var tokens = Tokenize(query);
        var merged = new Dictionary<string, Ranked>(StringComparer.Ordinal);

        foreach (var passage in keywordCandidates)
            Consider(merged, passage, query, tokens, cosine: null);

        foreach (var (passage, cosine) in semanticCandidates)
            Consider(merged, passage, query, tokens, cosine);

        return merged.Values
            .OrderBy(r => r.Band)
            .ThenByDescending(r => r.Score)
            .Take(maxResults)
            .Select(r => new ScriptureSearchHit(r.Passage, r.Score, r.Kind))
            .ToList();
    }

    private static void Consider(
        Dictionary<string, Ranked> merged,
        ScripturePassage passage,
        string query,
        IReadOnlyList<string> tokens,
        double? cosine)
    {
        var phrase = HasPhrase(passage.Text, query);
        var matched = tokens.Count(t => HasKeyword(passage.Text, t));
        var hasKeyword = matched > 0;
        var lexical = phrase || hasKeyword;

        if (!lexical && cosine is { } weak && weak < MinSemanticScore)
            return;

        if (!lexical && cosine is null)
            return;

        var keywordScore = phrase
            ? 1.0
            : tokens.Count == 0 ? 0 : (double)matched / tokens.Count;

        double score;
        string kind;
        if (lexical && cosine is { } sim)
        {
            score = Math.Min(1.0, phrase ? 1.0 : sim * 0.8 + keywordScore * 0.5);
            kind = ScriptureSearchHit.KindHybrid;
        }
        else if (lexical)
        {
            score = keywordScore;
            kind = ScriptureSearchHit.KindKeyword;
        }
        else
        {
            score = cosine!.Value;
            kind = ScriptureSearchHit.KindSemantic;
        }

        var band = lexical ? 0 : 1;
        var key = $"{passage.Book}|{passage.Chapter}|{passage.VerseStart}";
        if (merged.TryGetValue(key, out var existing) && Better(existing, band, score))
            return;

        merged[key] = new Ranked(passage, score, kind, band);
    }

    private static bool Better(Ranked existing, int band, double score)
        => existing.Band < band || (existing.Band == band && existing.Score >= score);

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var chars = text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private readonly record struct Ranked(ScripturePassage Passage, double Score, string Kind, int Band);
}
