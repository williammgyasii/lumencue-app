using ChurchProjection.Core.Models.Theme;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

// The Theme Studio used to label every shape "Shape 1", "Shape 2"… which tells the operator nothing.
// ThemeLayerNaming.DefaultLabel gives a shape a meaningful auto-name based on what it is: an image
// graphic, a thin accent bar, or a plain rectangle/panel. An operator-set Name always wins over this.
public class ThemeLayerNamingTests
{
    [Fact]
    public void Shape_with_an_image_reads_as_Image()
    {
        var shape = new ThemeShape { ImagePath = "lower-third.png", Width = 1920, Height = 1080 };
        Assert.Equal("Image", ThemeLayerNaming.DefaultLabel(shape));
    }

    [Fact]
    public void A_thin_shape_reads_as_Bar()
    {
        // The AddBar default: a 600x8 accent line.
        var shape = new ThemeShape { Width = 600, Height = 8 };
        Assert.Equal("Bar", ThemeLayerNaming.DefaultLabel(shape));
    }

    [Fact]
    public void A_chunky_shape_reads_as_Rectangle()
    {
        // The AddShape default: a 1000x140 panel.
        var shape = new ThemeShape { Width = 1000, Height = 140 };
        Assert.Equal("Rectangle", ThemeLayerNaming.DefaultLabel(shape));
    }

    [Fact]
    public void An_image_wins_even_when_the_shape_is_thin()
    {
        var shape = new ThemeShape { ImagePath = "strip.png", Width = 1920, Height = 12 };
        Assert.Equal("Image", ThemeLayerNaming.DefaultLabel(shape));
    }

    [Fact]
    public void Blank_image_path_is_ignored()
    {
        var shape = new ThemeShape { ImagePath = "   ", Width = 1000, Height = 140 };
        Assert.Equal("Rectangle", ThemeLayerNaming.DefaultLabel(shape));
    }
}
