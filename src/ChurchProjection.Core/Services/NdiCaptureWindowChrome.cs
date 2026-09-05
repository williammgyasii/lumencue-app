using ChurchProjection.Core.Models.Theme;

namespace ChurchProjection.Core.Services;

/// <summary>
/// NDI capture window must stay full canvas so Avalonia can paint frames,
/// but it must not cover or block the operator desktop when Windows snaps it on-screen.
/// </summary>
public static class NdiCaptureWindowChrome
{
    public const double Opacity = 0;
    public const bool ShowActivated = false;
    public const bool ShowInTaskbar = false;
    public const bool IsHitTestVisible = false;
    public const bool IsMinimized = false;
    public const double Width = Theme.CanvasWidth;
    public const double Height = Theme.CanvasHeight;
    public const int FpsNumerator = 15;
    public const int FpsDenominator = 1;
}
