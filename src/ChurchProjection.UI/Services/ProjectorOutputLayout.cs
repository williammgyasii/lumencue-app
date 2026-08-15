using System;
using ChurchProjection.UI.ViewModels;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Maps a detected display onto a projector window. Physical outputs are sized to the
/// screen in DIPs (pixels ÷ scaling) so Retina and 1080p/1440p monitors fill edge-to-edge.
/// macOS Spaces fullscreen is skipped because it often leaves the content at canvas size.
/// </summary>
public readonly record struct ProjectorWindowPlacement(
    double Width,
    double Height,
    int PixelX,
    int PixelY,
    bool Decorated,
    bool UsePlatformFullScreen);

public static class ProjectorOutputLayout
{
    public static ProjectorWindowPlacement For(DisplayOption display) =>
        For(display, OperatingSystem.IsMacOS());

    public static ProjectorWindowPlacement For(DisplayOption display, bool isMacOs)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (display.IsWindowedPreview)
        {
            return new ProjectorWindowPlacement(
                Width: display.Width,
                Height: display.Height,
                PixelX: display.X,
                PixelY: display.Y,
                Decorated: true,
                UsePlatformFullScreen: false);
        }

        var scale = display.Scaling > 0 ? display.Scaling : 1;
        return new ProjectorWindowPlacement(
            Width: display.Width / scale,
            Height: display.Height / scale,
            PixelX: display.X,
            PixelY: display.Y,
            Decorated: false,
            UsePlatformFullScreen: !isMacOs);
    }
}
