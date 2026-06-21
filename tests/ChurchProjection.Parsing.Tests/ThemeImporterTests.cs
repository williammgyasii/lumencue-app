using ChurchProjection.Core.Models.Theme;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

// Pins the importer that turns a church's designed lower-third graphic (any size) into a Theme
// we render at 1920x1080 and send to an ATEM.
//
// Geometry rule (fixes the client's "uniform scale shrinks my design"): the client's canvas is
// NOT our output size (their sample was 1024x576). We always output 1920x1080, so the graphic is
// mapped onto the canvas at FULL WIDTH, preserving its native aspect ratio, anchored to the
// BOTTOM. A 16:9 export then fills the frame exactly; a low-res/odd-ratio one still spans the
// full width at its true proportions — never letterboxed, never distorted.
//
// Background rule: the frame's empty area is the ATEM KEY COLOUR (green) — that is the
// "transparent" the operator means; the switcher keys it out over the live camera. Our text is
// overlaid on top of the imported graphic.
public class ThemeImporterTests
{
    private const double CanvasW = 1920;
    private const double CanvasH = 1080;
    private const double Tol = 0.5; // design-pixel tolerance

    // A true 16:9 graphic maps to the whole 1920x1080 frame regardless of its source pixel count,
    // so the 1024x576 sample stops landing at native size in the corner and fills the frame.
    [Fact]
    public void Sixteen_by_nine_graphic_fills_the_whole_1920x1080_frame_even_when_low_res()
    {
        var theme = ThemeImporter.FromImage("Sunday LT", "C:/art/lt.png", 1024, 576);

        var shape = Assert.Single(theme.Shapes);
        Assert.Equal("C:/art/lt.png", shape.ImagePath);

        Assert.Equal(0, shape.X, Tol);
        Assert.Equal(0, shape.Y, Tol);
        Assert.Equal(CanvasW, shape.Width, Tol);
        Assert.Equal(CanvasH, shape.Height, Tol);
    }

    // A trimmed/odd-ratio band spans the full width at its true proportions and sits at the
    // bottom — NOT shrunk to fit inside the frame.
    [Fact]
    public void Odd_ratio_band_spans_full_width_and_anchors_to_the_bottom()
    {
        var theme = ThemeImporter.FromImage("Trimmed LT", "C:/art/band.png", 1600, 280);

        var shape = Assert.Single(theme.Shapes);
        var expectedHeight = CanvasW * 280.0 / 1600.0; // 336

        Assert.Equal(0, shape.X, Tol);
        Assert.Equal(CanvasW, shape.Width, Tol);
        Assert.Equal(expectedHeight, shape.Height, Tol);
        Assert.Equal(CanvasH - expectedHeight, shape.Y, Tol); // bottom-anchored
    }

    // The art must keep its native proportions — the whole point is "don't distort/shrink it".
    [Fact]
    public void Imported_shape_preserves_the_source_aspect_ratio()
    {
        var theme = ThemeImporter.FromImage("AR LT", "C:/art/band.png", 1600, 280);

        var shape = Assert.Single(theme.Shapes);
        Assert.Equal(1600.0 / 280.0, shape.Width / shape.Height, 3);
    }

    // The frame's empty area is the ATEM key colour so the switcher composites it over live video.
    [Fact]
    public void Imports_with_a_green_key_background_for_the_atem()
    {
        var theme = ThemeImporter.FromImage("Sunday LT", "C:/art/lt.png", 1024, 576);

        Assert.Equal(ThemeBackgroundKind.KeyColorGreen, theme.BackgroundKind);
    }

    // The "put our text on the graphic they sent" ask: the importer seeds visible heading (Title)
    // + verse (Body) regions so the operator has something to position over the graphic.
    [Fact]
    public void Seeds_visible_heading_and_verse_text_regions()
    {
        var theme = ThemeImporter.FromImage("Sunday LT", "C:/art/lt.png", 1920, 1080);

        Assert.NotNull(theme.TitleRegion);
        Assert.NotNull(theme.BodyRegion);
        Assert.True(theme.TitleRegion!.Visible);
        Assert.True(theme.BodyRegion!.Visible);
    }
}
