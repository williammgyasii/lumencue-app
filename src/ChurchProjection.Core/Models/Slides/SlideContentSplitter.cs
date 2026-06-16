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
}
