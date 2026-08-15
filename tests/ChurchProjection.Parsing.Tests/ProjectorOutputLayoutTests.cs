using ChurchProjection.UI.Services;
using ChurchProjection.UI.ViewModels;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ProjectorOutputLayoutTests
{
    [Fact]
    public void PhysicalDisplay_FillsPixelBounds_AtScale1()
    {
        var display = new DisplayOption("Display 2 — 2560×1440", 1920, 0, 2560, 1440, Scaling: 1);
        var place = ProjectorOutputLayout.For(display, isMacOs: true);

        Assert.Equal(2560, place.Width);
        Assert.Equal(1440, place.Height);
        Assert.Equal(1920, place.PixelX);
        Assert.Equal(0, place.PixelY);
        Assert.False(place.Decorated);
        Assert.False(place.UsePlatformFullScreen);
    }

    [Fact]
    public void PhysicalDisplay_ConvertsRetinaPixelsToDips()
    {
        var display = new DisplayOption("Retina", 0, 0, 2560, 1440, Scaling: 2);
        var place = ProjectorOutputLayout.For(display, isMacOs: true);

        Assert.Equal(1280, place.Width);
        Assert.Equal(720, place.Height);
    }

    [Fact]
    public void PhysicalDisplay_UsesPlatformFullScreenOffMac()
    {
        var display = new DisplayOption("Display 1", 0, 0, 1920, 1080, Scaling: 1);
        var windows = ProjectorOutputLayout.For(display, isMacOs: false);
        var mac = ProjectorOutputLayout.For(display, isMacOs: true);

        Assert.True(windows.UsePlatformFullScreen);
        Assert.False(mac.UsePlatformFullScreen);
        Assert.Equal(1920, windows.Width);
        Assert.Equal(1080, windows.Height);
    }

    [Fact]
    public void WindowedPreview_KeepsFixedSizeAndDecorations()
    {
        var display = new DisplayOption("Windowed preview", 80, 80, 960, 540, IsWindowedPreview: true);
        var place = ProjectorOutputLayout.For(display, isMacOs: true);

        Assert.Equal(960, place.Width);
        Assert.Equal(540, place.Height);
        Assert.True(place.Decorated);
        Assert.False(place.UsePlatformFullScreen);
    }
}
