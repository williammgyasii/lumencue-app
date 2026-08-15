using ChurchProjection.UI.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class NdiPixelConvertTests
{
    [Fact]
    public void RgbaGold_BecomesBgraForNdi()
    {
        // R, G, B, A — a gold pixel as Avalonia/Skia often stores it on Mac.
        var pixels = new byte[] { 200, 120, 30, 255 };

        NdiPixelConvert.ToStraightOpaqueBgra(pixels, 1, 1, 4, sourceIsRgba: true);

        Assert.Equal(30, pixels[0]);
        Assert.Equal(120, pixels[1]);
        Assert.Equal(200, pixels[2]);
        Assert.Equal(255, pixels[3]);
    }

    [Fact]
    public void BgraGold_StaysBgra()
    {
        var pixels = new byte[] { 30, 120, 200, 255 };

        NdiPixelConvert.ToStraightOpaqueBgra(pixels, 1, 1, 4, sourceIsRgba: false);

        Assert.Equal(30, pixels[0]);
        Assert.Equal(120, pixels[1]);
        Assert.Equal(200, pixels[2]);
        Assert.Equal(255, pixels[3]);
    }

    [Fact]
    public void PremultipliedRgba_UnpremultipliesThenSwaps()
    {
        // 50% red in premultiplied RGBA: R=128, G=0, B=0, A=128
        var pixels = new byte[] { 128, 0, 0, 128 };

        NdiPixelConvert.ToStraightOpaqueBgra(pixels, 1, 1, 4, sourceIsRgba: true);

        Assert.Equal(0, pixels[0]);
        Assert.Equal(0, pixels[1]);
        Assert.Equal(255, pixels[2]);
        Assert.Equal(255, pixels[3]);
    }
}
