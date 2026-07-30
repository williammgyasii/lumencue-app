using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Models.Slides;

/// <summary>
/// Plans note slide bodies without UI measurement. <see cref="NoteSplitMode.AutoFit"/> is handled
/// separately by <c>DeckBuilder</c>, which packs text to the theme.
/// </summary>
public static class NoteSlidePlanner
{
    public static IReadOnlyList<string> PlanBodies(string body, NoteSplitMode mode)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        return mode switch
        {
            NoteSplitMode.BySection => PlanBySection(body),
            NoteSplitMode.OneParagraphPerSlide => SlideContentSplitter.SplitNoteParagraphs(body),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Use DeckBuilder for AutoFit."),
        };
    }

    private static IReadOnlyList<string> PlanBySection(string body)
    {
        var slides = new List<string>();
        foreach (var section in SlideContentSplitter.SplitNoteSections(body))
            slides.AddRange(SlideContentSplitter.SplitNoteParagraphs(section));
        return slides;
    }
}
