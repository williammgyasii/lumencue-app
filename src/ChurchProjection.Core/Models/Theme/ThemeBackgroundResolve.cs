namespace ChurchProjection.Core.Models.Theme;

/// <summary>What the projector should paint behind the slide.</summary>
public enum ThemeBackgroundPaint
{
    SolidOrKey,
    ThemeImage,
    LiveMedia,
    PlaceholderStandIn,
}

/// <summary>
/// Live media only shows through a Placeholder theme. If that theme has no selected
/// background, Program still needs a visible stand-in so the hole is obvious.
/// </summary>
public static class ThemeBackgroundResolve
{
    public static ThemeBackgroundPaint Choose(
        ThemeBackgroundKind kind,
        bool hasLiveFrame,
        bool hasThemeImage)
    {
        if (kind == ThemeBackgroundKind.Placeholder)
            return hasLiveFrame ? ThemeBackgroundPaint.LiveMedia : ThemeBackgroundPaint.PlaceholderStandIn;

        if (kind == ThemeBackgroundKind.Image && hasThemeImage)
            return ThemeBackgroundPaint.ThemeImage;

        return ThemeBackgroundPaint.SolidOrKey;
    }

    /// <summary>
    /// Operator background tiles only apply (and only show a selection ring) on Placeholder themes.
    /// Solid / image / key themes already own their backdrop.
    /// </summary>
    public static bool AcceptsLiveSelection(ThemeBackgroundKind kind)
        => kind == ThemeBackgroundKind.Placeholder;
}
