using System;

namespace ChurchProjection.Core.Models.Theme;

/// <summary>
/// Pure placement geometry for the fixed 1920x1080 theme canvas: decides where an object's top-left
/// corner may sit when it is moved. Kept free of any UI/Avalonia dependency so it is unit-testable
/// and shared by Theme Studio's drag-to-move and the X/Y property setters.
/// </summary>
public static class ThemePlacement
{
    /// <summary>How many design-pixels of a bleeding object must always stay on-screen, so an
    /// imported graphic can never be dragged completely out of sight.</summary>
    public const double DefaultMinVisible = 60;

    /// <summary>
    /// Clamps a rectangle's top-left (<paramref name="x"/>, <paramref name="y"/>) to a placeable spot
    /// on the canvas.
    /// <para>When <paramref name="allowBleed"/> is <c>false</c> (text regions) the rectangle is kept
    /// fully inside the canvas — the existing behaviour.</para>
    /// <para>When <c>true</c> (decorative shapes / imported lower-third graphics) the rectangle may
    /// extend past the edges, as long as at least <paramref name="minVisible"/> px remain on-screen on
    /// each axis — so a frame-filling design can be nudged up/down/sideways into place without a
    /// Photoshop round-trip, and can never be lost entirely off-screen.</para>
    /// </summary>
    public static (double X, double Y) ClampPosition(
        double x, double y, double width, double height,
        double canvasWidth, double canvasHeight,
        bool allowBleed, double minVisible = DefaultMinVisible)
    {
        double minX, maxX, minY, maxY;
        if (allowBleed)
        {
            // The object may hang off any edge, but at least minVisible px must remain on-screen on
            // each axis so it can never be dragged completely out of sight.
            minX = minVisible - width;
            maxX = canvasWidth - minVisible;
            minY = minVisible - height;
            maxY = canvasHeight - minVisible;
        }
        else
        {
            // Fully inside the canvas (text regions must stay where they can be read).
            minX = 0;
            maxX = canvasWidth - width;
            minY = 0;
            maxY = canvasHeight - height;
        }

        return (Clamp(x, minX, maxX), Clamp(y, minY, maxY));
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min) max = min; // degenerate (object spans more than the allowed range): pin to min
        return value < min ? min : value > max ? max : value;
    }
}
