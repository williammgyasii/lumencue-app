using System.Globalization;
using Avalonia.Media;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Theme;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Turns a single piece of content into a paged <see cref="SlideDeck"/>, packing as many
/// verses / stanzas as cleanly fit on each page for the given theme. Measurement is done with
/// <see cref="FormattedText"/> against a canonical 1920x1080 canvas so the result is deterministic
/// regardless of the actual output resolution (the renderer scales to fit).
/// </summary>
public static class DeckBuilder
{
    /// <summary>
    /// Optional hard cap on characters per page (0 = off). When set, pages are split so no page
    /// exceeds this length — used to keep text small enough for a downstream ProPresenter message
    /// box (so long passages page instead of being clipped). Applies to every deck built.
    /// </summary>
    public static int MaxCharsPerSlide { get; set; }

    /// <summary>Builds a note deck using the note's chosen split mode.</summary>
    public static SlideDeck BuildNote(string title, string body, string footer, Theme theme, NoteSplitMode splitMode)
    {
        if (splitMode == NoteSplitMode.AutoFit)
            return Build(SlideType.Note, title, body, footer, theme);

        var bodies = NoteSlidePlanner.PlanBodies(body, splitMode);
        if (bodies.Count == 0)
            return SlideDeck.Single(new Slide { Type = SlideType.Note, Title = title, Body = body, Footer = footer });

        var slides = bodies
            .Select(b => new Slide { Type = SlideType.Note, Title = title, Body = b, Footer = footer })
            .ToList();
        return new SlideDeck(slides);
    }

    public static SlideDeck Build(SlideType type, string title, string body, string footer, Theme theme, int linesPerSlide = 0)
    {
        // Per-song override: pack a fixed number of lyric lines per slide (within each stanza)
        // instead of the theme's automatic text-fit.
        if (linesPerSlide > 0)
        {
            var linePages = SplitByLines(body, linesPerSlide);
            var lineSlides = linePages
                .Select(p => new Slide { Type = type, Title = title, Body = p, Footer = footer })
                .ToList();
            return lineSlides.Count > 0
                ? new SlideDeck(lineSlides)
                : SlideDeck.Single(new Slide { Type = type, Title = title, Body = body, Footer = footer });
        }

        var blocks = SlideContentSplitter.SplitBlocks(type, body);
        if (blocks.Count == 0)
            return SlideDeck.Single(new Slide { Type = type, Title = title, Body = body, Footer = footer });

        var typeface = new Typeface(ResolveFont(theme.FontFamily), FontStyle.Normal,
            theme.Bold ? FontWeight.Bold : FontWeight.Normal);

        // Paginate against the body region so a smaller/repositioned text box pages correctly.
        var bodyRegion = theme.ResolvePaginationRegion(type);

        // Pagination must agree with how the text is actually rendered. When the body auto-fits, the
        // renderer can shrink text down to MinFontSize, so we paginate at that smallest size — this
        // keeps a normal verse whole on one slide (the renderer then grows it to fill the box) instead
        // of splitting it mid-sentence at the larger BodyFontSize. Non-auto-fit themes paginate at
        // their fixed size exactly as before.
        var fitFontSize = bodyRegion.AutoFit ? Math.Max(1, bodyRegion.MinFontSize) : theme.BodyFontSize;
        var lineHeight = fitFontSize * theme.LineHeightMultiplier;

        // The renderer insets the text by the region's padding, so subtract it from the usable box
        // (otherwise pagination thinks more fits than really does and the text overflows/clips).
        var maxWidth = Math.Max(100, bodyRegion.Width - bodyRegion.TextPaddingX * 2);
        var maxHeight = Math.Max(80, bodyRegion.Height - bodyRegion.TextPaddingY * 2);
        var separator = type == SlideType.Scripture ? " " : "\n\n";
        var maxChars = MaxCharsPerSlide;

        var pages = PackBlocks(blocks, separator, typeface, fitFontSize, maxWidth, maxHeight, lineHeight, maxChars);

        var slides = pages
            .Select(p => new Slide { Type = type, Title = title, Body = p, Footer = footer })
            .ToList();

        return new SlideDeck(slides);
    }

    private static List<string> PackBlocks(
        IReadOnlyList<string> blocks, string separator, Typeface typeface,
        double fontSize, double maxWidth, double maxHeight, double lineHeight, int maxChars)
    {
        var pages = new List<string>();
        var current = "";

        foreach (var block in blocks)
        {
            var candidate = current.Length == 0 ? block : current + separator + block;
            if (Fits(candidate, typeface, fontSize, maxWidth, maxHeight, lineHeight, maxChars))
            {
                current = candidate;
                continue;
            }

            // Candidate overflowed: flush what we have, then place the block on its own.
            if (current.Length > 0)
            {
                pages.Add(current);
                current = "";
            }

            if (Fits(block, typeface, fontSize, maxWidth, maxHeight, lineHeight, maxChars))
            {
                current = block;
            }
            else
            {
                // A single block is taller than one page (or over the char cap): split by words.
                foreach (var piece in WrapOversizedBlock(block, typeface, fontSize, maxWidth, maxHeight, lineHeight, maxChars))
                    pages.Add(piece);
            }
        }

        if (current.Length > 0)
            pages.Add(current);

        return pages.Count > 0 ? pages : [string.Join(separator, blocks)];
    }

    private static IEnumerable<string> WrapOversizedBlock(
        string block, Typeface typeface, double fontSize, double maxWidth, double maxHeight, double lineHeight, int maxChars)
    {
        var words = block.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = "";

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (Fits(candidate, typeface, fontSize, maxWidth, maxHeight, lineHeight, maxChars))
            {
                current = candidate;
            }
            else
            {
                if (current.Length > 0)
                    yield return current;
                current = word; // guarantee forward progress even if one word is huge
            }
        }

        if (current.Length > 0)
            yield return current;
    }

    private static bool Fits(string text, Typeface typeface, double fontSize, double maxWidth, double maxHeight, double lineHeight, int maxChars)
    {
        // Hard character cap (when enabled) keeps pages small enough for a downstream message box.
        if (maxChars > 0 && text.Length > maxChars)
            return false;

        var ft = new FormattedText(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White)
        {
            MaxTextWidth = maxWidth,
            Trimming = TextTrimming.None,
        };
        if (lineHeight > 0)
            ft.LineHeight = lineHeight;

        return ft.Height <= maxHeight;
    }

    /// <summary>Chunks each stanza into pages of at most <paramref name="linesPerSlide"/> lyric lines.</summary>
    private static List<string> SplitByLines(string body, int linesPerSlide)
    {
        var normalized = body.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var pages = new List<string>();

        foreach (var stanza in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = stanza.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            for (int i = 0; i < lines.Count; i += linesPerSlide)
                pages.Add(string.Join("\n", lines.Skip(i).Take(linesPerSlide)));
        }

        return pages;
    }

    private static FontFamily ResolveFont(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FontFamily.Default;
        try { return new FontFamily(name); }
        catch { return FontFamily.Default; }
    }
}
