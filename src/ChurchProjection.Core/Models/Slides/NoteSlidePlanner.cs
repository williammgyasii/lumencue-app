using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Models.Slides;

/// <summary>
/// Plans note slide bodies without UI measurement. <see cref="NoteSplitMode.AutoFit"/> is handled
/// separately by <c>DeckBuilder</c>, which packs text to the theme.
/// </summary>
public static class NoteSlidePlanner
{
    public static IReadOnlyList<string> PlanBodies(string body, NoteSplitMode mode, int linesPerSlide = 0)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        if (linesPerSlide > 0)
            return SplitByLineCount(body, linesPerSlide);

        return mode switch
        {
            NoteSplitMode.BySection => PlanBySection(body),
            NoteSplitMode.OneParagraphPerSlide => SlideContentSplitter.SplitNoteParagraphs(body),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Use DeckBuilder for AutoFit."),
        };
    }

    /// <summary>
    /// Packs every non-blank line (blank lines are ignored) into slides of
    /// <paramref name="linesPerSlide"/> lines. Notes are usually one thought per line/paragraph,
    /// so song-style stanza grouping would leave Lines=2 looking like a no-op.
    /// </summary>
    public static IReadOnlyList<string> SplitByLineCount(string body, int linesPerSlide)
    {
        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count == 0) return [];
        if (linesPerSlide <= 0) return [string.Join("\n", lines)];

        var pages = new List<string>();
        for (var i = 0; i < lines.Count; i += linesPerSlide)
            pages.Add(string.Join("\n", lines.Skip(i).Take(linesPerSlide)));
        return pages;
    }

    private static IReadOnlyList<string> PlanBySection(string body)
    {
        var slides = new List<string>();
        foreach (var section in SlideContentSplitter.SplitNoteSections(body))
            slides.AddRange(SlideContentSplitter.SplitNoteParagraphs(section));
        return slides;
    }
}
