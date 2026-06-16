using System.Text.RegularExpressions;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Data;
using Serilog;

namespace ChurchProjection.Infrastructure.Search;

/// <summary>
/// Smart, in-memory lyric search over the local song library. Scoring blends an exact-phrase bonus,
/// token coverage (how many query words are present), prefix matching, and typo-tolerant fuzzy
/// matching (bounded edit distance). Designed to be fast enough to run on every keystroke.
/// </summary>
public sealed partial class SongSearchService : ISongSearchService
{
    private readonly SongRepository _songs;

    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private List<SongEntry> _index = [];
    private volatile bool _dirty = true;

    public SongSearchService(SongRepository songs)
    {
        _songs = songs;
        _songs.Changed += Invalidate; // re-index after any local edit
    }

    public void Invalidate() => _dirty = true;

    [GeneratedRegex(@"[^a-z0-9']+", RegexOptions.Compiled)]
    private static partial Regex NonWord();

    // ───────────────────────── Index ─────────────────────────

    private sealed class LineEntry
    {
        public required string Original;
        public required string Norm;
        public required HashSet<string> Tokens;
    }

    private sealed class SectionEntry
    {
        public required SongSection Section;
        public required string Norm;
        public required HashSet<string> Tokens;
        public required List<LineEntry> Lines;
    }

    private sealed class SongEntry
    {
        public required Song Song;
        public required string TitleNorm;
        public required HashSet<string> TitleTokens;
        public required List<SectionEntry> Sections;
    }

    private static string Normalize(string text) =>
        NonWord().Replace(text.ToLowerInvariant(), " ").Trim();

    private static HashSet<string> Tokenize(string norm) =>
        norm.Length == 0 ? [] : [.. norm.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    private async Task EnsureIndexAsync()
    {
        if (!_dirty) return;
        await _buildLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_dirty) return;

            var songs = await _songs.GetAllAsync().ConfigureAwait(false);
            var index = new List<SongEntry>(songs.Count);

            foreach (var song in songs)
            {
                var sections = new List<SectionEntry>(song.Sections.Count);
                foreach (var section in song.Sections)
                {
                    var lines = new List<LineEntry>();
                    foreach (var raw in section.Text.Split('\n'))
                    {
                        var original = raw.Trim();
                        if (original.Length == 0) continue;
                        var norm = Normalize(original);
                        lines.Add(new LineEntry { Original = original, Norm = norm, Tokens = Tokenize(norm) });
                    }

                    var sectionNorm = Normalize(section.Text);
                    sections.Add(new SectionEntry
                    {
                        Section = section,
                        Norm = sectionNorm,
                        Tokens = Tokenize(sectionNorm),
                        Lines = lines,
                    });
                }

                var titleNorm = Normalize($"{song.Title} {song.Artist}");
                index.Add(new SongEntry
                {
                    Song = song,
                    TitleNorm = titleNorm,
                    TitleTokens = Tokenize(titleNorm),
                    Sections = sections,
                });
            }

            _index = index;
            _dirty = false;
            Log.Debug("Song search index built: {Songs} songs", index.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to build song search index");
        }
        finally
        {
            _buildLock.Release();
        }
    }

    // ───────────────────────── Search ─────────────────────────

    public async Task<IReadOnlyList<SongSearchHit>> SearchAsync(string query, int maxResults = 30, CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync().ConfigureAwait(false);
        var index = _index;

        var qNorm = Normalize(query ?? "");
        var qTokens = Tokenize(qNorm).ToList();

        // Empty query → whole library, alphabetical (acts as a browser).
        if (qTokens.Count == 0)
        {
            return index
                .OrderBy(e => e.Song.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(e => new SongSearchHit(e.Song, e.Sections.FirstOrDefault()?.Section,
                    e.Sections.FirstOrDefault()?.Lines.FirstOrDefault()?.Original ?? "", 0))
                .ToList();
        }

        var hits = new List<SongSearchHit>();

        foreach (var entry in index)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double bestSectionScore = 0;
            SectionEntry? bestSection = null;
            string bestSnippet = "";

            foreach (var section in entry.Sections)
            {
                var (score, snippet) = ScoreUnit(section.Lines, section.Norm, section.Tokens, qNorm, qTokens);
                if (score > bestSectionScore)
                {
                    bestSectionScore = score;
                    bestSection = section;
                    bestSnippet = snippet;
                }
            }

            // Title/artist match boosts the song even when lyrics don't contain the words.
            var titleScore = TokenCoverage(entry.TitleTokens, qTokens) * 70
                             + (entry.TitleNorm.Contains(qNorm) ? 80 : 0);

            var songScore = Math.Max(bestSectionScore, titleScore);
            if (songScore < 12) continue; // below this is noise

            bestSection ??= entry.Sections.FirstOrDefault();
            if (string.IsNullOrEmpty(bestSnippet))
                bestSnippet = bestSection?.Lines.FirstOrDefault()?.Original ?? entry.Song.Title;

            hits.Add(new SongSearchHit(entry.Song, bestSection?.Section, bestSnippet, songScore));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Song.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>Scores a unit (a section's lines, or a title) and returns the best matching line.</summary>
    private static (double Score, string Snippet) ScoreUnit(
        List<LineEntry> lines, string unitNorm, HashSet<string> unitTokens, string qNorm, List<string> qTokens)
    {
        // Exact phrase containment is the strongest signal.
        double phraseBonus = qNorm.Length >= 3 && unitNorm.Contains(qNorm) ? 120 : 0;

        // Coverage across the whole unit (handles queries that span multiple lines).
        double unitCoverage = TokenCoverage(unitTokens, qTokens);

        double bestLineScore = 0;
        string snippet = lines.Count > 0 ? lines[0].Original : "";

        foreach (var line in lines)
        {
            var coverage = TokenCoverage(line.Tokens, qTokens);
            var linePhrase = qNorm.Length >= 3 && line.Norm.Contains(qNorm) ? 0.6 : 0;
            var lineScore = coverage + linePhrase;
            if (lineScore > bestLineScore)
            {
                bestLineScore = lineScore;
                snippet = line.Original;
            }
        }

        var score = phraseBonus + unitCoverage * 60 + bestLineScore * 45;
        return (score, snippet);
    }

    /// <summary>Fraction (0..1) of query tokens present, with partial credit for prefix/fuzzy matches.</summary>
    private static double TokenCoverage(HashSet<string> haystack, List<string> qTokens)
    {
        if (qTokens.Count == 0 || haystack.Count == 0) return 0;

        double sum = 0;
        foreach (var q in qTokens)
        {
            double best = 0;
            foreach (var t in haystack)
            {
                if (t == q) { best = 1.0; break; }
                if (t.StartsWith(q, StringComparison.Ordinal) || q.StartsWith(t, StringComparison.Ordinal))
                    best = Math.Max(best, 0.85);
                else if (best < 0.6 && IsFuzzyMatch(t, q))
                    best = Math.Max(best, 0.6);
            }
            sum += best;
        }
        return sum / qTokens.Count;
    }

    /// <summary>Typo tolerance: bounded edit distance scaled to token length.</summary>
    private static bool IsFuzzyMatch(string a, string b)
    {
        var threshold = Math.Min(a.Length, b.Length) switch
        {
            <= 3 => 0,
            <= 5 => 1,
            <= 8 => 2,
            _ => 3,
        };
        if (threshold == 0) return false;
        if (Math.Abs(a.Length - b.Length) > threshold) return false;
        return BoundedLevenshtein(a, b, threshold) <= threshold;
    }

    private static int BoundedLevenshtein(string a, string b, int max)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            int rowMin = curr[0];
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                if (curr[j] < rowMin) rowMin = curr[j];
            }
            if (rowMin > max) return max + 1; // can only grow; bail early
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
