using Avalonia.Media;
using ChurchProjection.UI.Converters;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class HexColorConverterTests
{
    [Fact]
    public void ConvertBack_WritesStableHexSoThePreviewCanBindLive()
    {
        var hex = HexColorConverter.Instance.ConvertBack(
            Color.FromArgb(0xFF, 0x10, 0x14, 0x18),
            typeof(string),
            null,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("#FF101418", hex);
    }

    [Fact]
    public void Convert_ReadsHexIntoAColor()
    {
        var color = HexColorConverter.Instance.Convert(
            "#FF7C6CF6",
            typeof(Color),
            null,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(Color.FromArgb(0xFF, 0x7C, 0x6C, 0xF6), color);
    }
}
