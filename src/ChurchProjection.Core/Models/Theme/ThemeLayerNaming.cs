namespace ChurchProjection.Core.Models.Theme;

/// <summary>
/// Derives a human-friendly default label for a <see cref="ThemeShape"/> in the Theme Studio's
/// OBJECTS list. An operator-set <see cref="ThemeShape.Name"/> always takes precedence; this is only
/// the fallback so layers read as "Image" / "Bar" / "Rectangle" instead of "Shape 1".
/// </summary>
public static class ThemeLayerNaming
{
    /// <summary>A shape this short (design px) reads as a thin accent bar rather than a panel.</summary>
    private const double BarHeightThreshold = 24;

    public static string DefaultLabel(ThemeShape shape)
    {
        if (!string.IsNullOrWhiteSpace(shape.ImagePath))
            return "Image";

        return shape.Height <= BarHeightThreshold ? "Bar" : "Rectangle";
    }
}
