namespace ChurchProjection.Core.Services;

/// <summary>
/// Conservative “does this text fit this box?” used so Lower Third pagination
/// cannot be more optimistic than the live renderer.
/// </summary>
public static class ThemeTextBox
{
    /// <summary>Wide-ish average glyph in ems so we underestimate chars per line.</summary>
    public const double AverageGlyphEm = 0.55;

    public static bool Fits(
        string text,
        double boxWidth,
        double boxHeight,
        double fontSize,
        double lineHeightMultiplier)
    {
        if (string.IsNullOrEmpty(text)) return true;
        var lineHeight = fontSize * lineHeightMultiplier;
        if (lineHeight <= 0 || boxWidth <= 0 || boxHeight <= 0) return false;

        var maxLines = Math.Max(1, (int)Math.Floor((boxHeight + 0.01) / lineHeight));
        var charsPerLine = Math.Max(1, (int)Math.Floor(boxWidth / (fontSize * AverageGlyphEm)));

        var lines = 0;
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var remaining = Math.Max(1, paragraph.Length);
            while (remaining > 0)
            {
                lines++;
                if (lines > maxLines) return false;
                remaining -= charsPerLine;
            }
        }

        return true;
    }
}
