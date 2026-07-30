using System.Text.RegularExpressions;

namespace ChurchProjection.Core.Models.Slides;

/// <summary>
/// Breaks a slide body into the smallest natural units that must stay together:
/// individual verses for scripture (split on <c>[n]</c> markers) and stanzas for songs
/// (split on blank lines). The UI deck builder then packs as many units as cleanly fit
/// onto each projected page.
/// </summary>
public static partial class SlideContentSplitter
{
    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex VerseMarker();

    public static IReadOnlyList<string> SplitBlocks(SlideType type, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        var normalized = body.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        return type switch
        {
            SlideType.Scripture => SplitScripture(normalized),
            SlideType.Lyric => SplitStanzas(normalized),
            _ => SplitStanzas(normalized),
        };
    }

    private static IReadOnlyList<string> SplitScripture(string body)
    {
        // Bodies built for whole passages look like "[1] In the beginning [2] ...".
        var matches = VerseMarker().Matches(body);
        if (matches.Count == 0)
            return [body];

        var blocks = new List<string>(matches.Count);
        for (int i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            var block = body[start..end].Trim();
            if (block.Length > 0)
                blocks.Add(block);
        }

        return blocks.Count > 0 ? blocks : [body];
    }

    private static IReadOnlyList<string> SplitStanzas(string body)
    {
        var stanzas = body
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        return stanzas.Count > 0 ? stanzas : [body];
    }

    /// <summary>
    /// Splits note paragraphs on blank lines, then peels a standalone ✍ teaching-attribution line
    /// onto its own slide when it heads a multi-line paragraph.
    /// </summary>
    public static IReadOnlyList<string> SplitNoteParagraphs(string body)
    {
        var slides = new List<string>();
        foreach (var paragraph in SplitStanzas(body.Replace("\r\n", "\n").Replace('\r', '\n').Trim()))
            slides.AddRange(PeelTeachingHeader(paragraph));
        return slides;
    }

    private static IReadOnlyList<string> PeelTeachingHeader(string paragraph)
    {
        var newline = paragraph.IndexOf('\n');
        if (newline < 0)
            return [paragraph];

        var firstLine = paragraph[..newline].Trim();
        if (!firstLine.StartsWith("✍") || !IsSectionHeader(firstLine))
            return [paragraph];

        var rest = paragraph[(newline + 1)..].Trim();
        return rest.Length > 0 ? [firstLine, rest] : [paragraph];
    }

    /// <summary>
    /// Splits a note on section headers (emoji markers or short ALL CAPS lines), keeping the header
    /// with the content that follows it.
    /// </summary>
    public static IReadOnlyList<string> SplitNoteSections(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        var normalized = body.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paragraphs.Count == 0)
            return [normalized];

        var sections = new List<string>();
        var current = paragraphs[0];

        for (var i = 1; i < paragraphs.Count; i++)
        {
            var para = paragraphs[i];
            var firstLine = para.Split('\n')[0].Trim();
            if (IsSectionHeader(firstLine))
            {
                sections.Add(current);
                current = para;
            }
            else
            {
                current += "\n\n" + para;
            }
        }

        sections.Add(current);
        return sections;
    }

    private static bool IsSectionHeader(string line)
    {
        if (line.Length == 0 || line.Length > 80)
            return false;

        if (line.StartsWith("📖") || line.StartsWith("✍") || line.StartsWith("🙏"))
            return true;

        return line.Length <= 40
               && line == line.ToUpperInvariant()
               && line.Any(char.IsLetter);
    }
}
