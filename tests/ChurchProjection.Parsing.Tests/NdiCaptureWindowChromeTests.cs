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

    [Fact]
    public void Capture_runs_at_fifteen_frames_per_second()
    {
        Assert.Equal(15, NdiCaptureWindowChrome.FpsNumerator);
        Assert.Equal(1, NdiCaptureWindowChrome.FpsDenominator);
        Assert.True(NdiCaptureWindowChrome.FpsNumerator < 30);
    }

    [Fact]
    public void Send_does_not_clock_video()
    {
        Assert.False(NdiCaptureWindowChrome.ClockVideo);
    }
}
