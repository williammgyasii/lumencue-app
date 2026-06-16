using System.Text.RegularExpressions;
using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Infrastructure.Parsing;

/// <summary>
/// Turns pasted lyrics into structured song sections. If the text carries explicit labels
/// (<c>Verse 1:</c>, <c>Chorus</c>, <c>[Bridge]</c>…), those are honored. Otherwise it auto-detects
/// structure: any stanza that repeats is treated as the Chorus, and the rest become Verse 1, 2, 3…
/// </summary>
public static partial class SongImportParser
{
    [GeneratedRegex(
        @"^\s*\[?\s*(verse\s*\d*|chorus|bridge|pre[- ]?chorus|tag|outro|intro|refrain|interlude|vamp|ending)\s*\d*\]?\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SectionHeaderPattern();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonAlphaNumeric();

    public static List<SongSection> ParseSections(string rawLyrics)
    {
        if (string.IsNullOrWhiteSpace(rawLyrics))
            return [];

        return HasAnyHeader(rawLyrics)
            ? ParseLabeled(rawLyrics)
            : ParseSmart(rawLyrics);
    }

    // ───────────────────────── Labeled lyrics (explicit section headers) ─────────────────────────

    private static List<SongSection> ParseLabeled(string rawLyrics)
    {
        var sections = new List<SongSection>();
        var lines = rawLyrics.Split('\n');

        string currentType = "verse";
        int verseCount = 0;
        int order = 0;
        var currentLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var match = SectionHeaderPattern().Match(line);

            if (match.Success)
            {
                if (currentLines.Count > 0)
                {
                    sections.Add(CreateSection(currentType, ref verseCount, order++, currentLines));
                    currentLines.Clear();
                }
                currentType = NormalizeSectionType(match.Groups[1].Value);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                currentLines.Add(line.Trim());
            }
            else if (currentLines.Count > 0)
            {
                sections.Add(CreateSection(currentType, ref verseCount, order++, currentLines));
                currentLines.Clear();
                currentType = "verse";
            }
        }

        if (currentLines.Count > 0)
            sections.Add(CreateSection(currentType, ref verseCount, order++, currentLines));

        if (sections.Count == 0 && !string.IsNullOrWhiteSpace(rawLyrics))
            sections.Add(new SongSection { SectionType = "verse", SectionOrder = 1, Text = rawLyrics.Trim() });

        return sections;
    }

    // ───────────────────────── Label-free lyrics (auto-detect structure) ─────────────────────────

    private static List<SongSection> ParseSmart(string rawLyrics)
    {
        var normalized = rawLyrics.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var blocks = SplitIntoBlocks(normalized);

        // Count how often each stanza appears (ignoring case/punctuation); repeats are the chorus.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in blocks)
        {
            var key = NormalizeKey(b);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var sections = new List<SongSection>();
        int verseCount = 0;
        int order = 0;

        foreach (var block in blocks)
        {
            var isChorus = counts[NormalizeKey(block)] >= 2;
            var lines = block.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            sections.Add(CreateSection(isChorus ? "chorus" : "verse", ref verseCount, order++, lines));
        }

        if (sections.Count == 0 && normalized.Length > 0)
            sections.Add(new SongSection { SectionType = "verse", SectionOrder = 1, Text = normalized });

        return sections;
    }

    // Verse spans longer than this are sub-divided so a single "verse" never becomes a wall of text.
    private const int MaxVerseLines = 8;
    private const int FallbackChunkLines = 4;
    // A repeated run must be at least this many lines to count as a chorus (avoids one stray repeated line).
    private const int MinChorusLines = 2;

    /// <summary>
    /// Splits lyrics into stanzas. Blank-line separation is honored when present. For a solid paste
    /// with no blank lines (the common worship-operator case), structure is inferred from repetition:
    /// the chorus is the longest run of lines that repeats, and the spans between repeats are verses.
    /// </summary>
    private static List<string> SplitIntoBlocks(string normalized)
    {
        var byBlankLine = normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (byBlankLine.Count > 1)
            return byBlankLine;

        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count <= FallbackChunkLines)
            return lines.Count > 0 ? [string.Join("\n", lines)] : [];

        return SegmentByRepetition(lines);
    }

    /// <summary>
    /// Infers song structure from a flat, blank-line-free line list by finding the longest contiguous
    /// run of lines that repeats (the chorus), then carving the remaining spans into verses.
    /// </summary>
    private static List<string> SegmentByRepetition(List<string> lines)
    {
        var n = lines.Count;
        var keys = lines.Select(NormalizeLineKey).ToArray();

        // Longest repeated, non-overlapping, contiguous run of lines.
        var bestLen = 0;
        var bestStart = -1;
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var k = 0;
                // Stop before the two windows overlap (i + k < j) and at the array end.
                while (i + k < j && j + k < n && keys[i + k].Length > 0 && keys[i + k] == keys[j + k])
                    k++;
                if (k > bestLen)
                {
                    bestLen = k;
                    bestStart = i;
                }
            }
        }

        // No meaningful repetition → fall back to even chunking (best we can do without structure).
        if (bestLen < MinChorusLines || bestStart < 0)
            return ChunkEvenly(lines, 0, n);

        var chorusKeys = keys.Skip(bestStart).Take(bestLen).ToArray();

        // Every non-overlapping occurrence of the chorus, left to right.
        var occurrences = new List<int>();
        for (var p = 0; p + bestLen <= n;)
        {
            var matches = true;
            for (var t = 0; t < bestLen; t++)
            {
                if (keys[p + t] != chorusKeys[t]) { matches = false; break; }
            }
            if (matches) { occurrences.Add(p); p += bestLen; }
            else p++;
        }

        var blocks = new List<string>();
        var pos = 0;
        foreach (var start in occurrences)
        {
            if (start > pos)
                AddVerseSpan(blocks, lines, pos, start);
            blocks.Add(string.Join("\n", lines.Skip(start).Take(bestLen)));
            pos = start + bestLen;
        }
        if (pos < n)
            AddVerseSpan(blocks, lines, pos, n);

        return blocks;
    }

    /// <summary>Emits a verse span as one block, sub-dividing only if it is unusually long.</summary>
    private static void AddVerseSpan(List<string> blocks, List<string> lines, int start, int end)
    {
        if (end - start <= MaxVerseLines)
        {
            blocks.Add(string.Join("\n", lines.Skip(start).Take(end - start)));
            return;
        }
        blocks.AddRange(ChunkEvenly(lines, start, end));
    }

    private static List<string> ChunkEvenly(List<string> lines, int start, int end)
    {
        var groups = new List<string>();
        for (var i = start; i < end; i += FallbackChunkLines)
            groups.Add(string.Join("\n", lines.Skip(i).Take(Math.Min(FallbackChunkLines, end - i))));
        return groups;
    }

    private static string NormalizeLineKey(string line) =>
        NonAlphaNumeric().Replace(line.ToLowerInvariant(), " ").Trim();

    private static bool HasAnyHeader(string rawLyrics)
    {
        foreach (var rawLine in rawLyrics.Split('\n'))
        {
            if (SectionHeaderPattern().Match(rawLine.TrimEnd('\r')).Success)
                return true;
        }
        return false;
    }

    private static string NormalizeKey(string block) =>
        NonAlphaNumeric().Replace(block.ToLowerInvariant(), " ").Trim();

    private static SongSection CreateSection(string type, ref int verseCount, int order, List<string> lines)
    {
        if (type == "verse") verseCount++;
        return new SongSection
        {
            SectionType = type,
            SectionOrder = type == "verse" ? verseCount : order + 1,
            Text = string.Join("\n", lines)
        };
    }

    private static string NormalizeSectionType(string raw)
    {
        var lower = raw.Trim().ToLowerInvariant();
        if (lower.StartsWith("verse")) return "verse";
        if (lower.Contains("pre") && lower.Contains("chorus")) return "pre-chorus";
        if (lower.StartsWith("chorus") || lower.StartsWith("refrain")) return "chorus";
        if (lower.StartsWith("bridge")) return "bridge";
        if (lower.StartsWith("tag")) return "tag";
        if (lower.StartsWith("outro") || lower.StartsWith("ending")) return "outro";
        if (lower.StartsWith("intro")) return "intro";
        return "verse";
    }
}
