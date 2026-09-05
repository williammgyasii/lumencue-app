using ChurchProjection.Core.Models.Theme;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class NdiCaptureWindowChromeTests
{
    [Fact]
    public void Capture_window_opacity_is_zero()
    {
        Assert.Equal(0, NdiCaptureWindowChrome.Opacity);
    }

    [Fact]
    public void Capture_window_does_not_activate_or_show_on_the_taskbar()
    {
        Assert.False(NdiCaptureWindowChrome.ShowActivated);
        Assert.False(NdiCaptureWindowChrome.ShowInTaskbar);
    }

    [Fact]
    public void Capture_window_does_not_eat_clicks()
    {
        Assert.False(NdiCaptureWindowChrome.IsHitTestVisible);
    }

    [Fact]
    public void Capture_stays_full_canvas_and_is_not_minimized()
    {
        Assert.Equal(Theme.CanvasWidth, NdiCaptureWindowChrome.Width);
        Assert.Equal(Theme.CanvasHeight, NdiCaptureWindowChrome.Height);
        Assert.False(NdiCaptureWindowChrome.IsMinimized);
    }
}
