using ChurchProjection.Core.Models.Theme;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

// The client imports a full-frame lower-third PNG — artwork drawn inside a 1920x1080 transparent
// canvas, with empty space below the artwork. Because the graphic fills the frame, the old "keep
// every object fully inside the canvas" clamp pinned it with zero slack, so they had to go back to
// Photoshop to shift the art down. ThemePlacement lets a decorative shape/image BLEED past the edges
// (a small margin always stays on-screen) so the operator can nudge it into place; text regions are
// unaffected and still stay fully inside the frame.
public class ThemePlacementTests
{
    private const double CanvasW = 1920;
    private const double CanvasH = 1080;
    private const double Tol = 0.5;
    private const double Margin = 60; // ThemePlacement.DefaultMinVisible

    // ── Text regions: unchanged — must stay fully inside the frame ──────────────────────────

    [Fact]
    public void Region_cannot_move_past_the_top_left_corner()
    {
        var (x, y) = ThemePlacement.ClampPosition(-50, -50, 600, 200, CanvasW, CanvasH, allowBleed: false);
        Assert.Equal(0, x, Tol);
        Assert.Equal(0, y, Tol);
    }

    [Fact]
    public void Region_cannot_move_past_the_bottom_right_corner()
    {
        var (x, y) = ThemePlacement.ClampPosition(5000, 5000, 600, 200, CanvasW, CanvasH, allowBleed: false);
        Assert.Equal(CanvasW - 600, x, Tol);
        Assert.Equal(CanvasH - 200, y, Tol);
    }

    // ── Imported full-frame graphic: can now be nudged, with bleed ──────────────────────────

    // The headline case: a 1920x1080 image starts pinned at (0,0); dragging it down 200px must take.
    [Fact]
    public void Full_frame_image_can_be_nudged_down()
    {
        var (x, y) = ThemePlacement.ClampPosition(0, 200, CanvasW, CanvasH, CanvasW, CanvasH, allowBleed: true);
        Assert.Equal(0, x, Tol);
        Assert.Equal(200, y, Tol);
    }

    // Dragged far down, only the top margin of the image stays on-screen (so it can't be lost).
    [Fact]
    public void Bleeding_shape_keeps_a_margin_visible_at_the_bottom()
    {
        var (_, y) = ThemePlacement.ClampPosition(0, 5000, CanvasW, CanvasH, CanvasW, CanvasH, allowBleed: true);
        Assert.Equal(CanvasH - Margin, y, Tol); // 1020: top 60px remain visible
    }

    // Dragged far up, the bottom margin (y + height) stays on-screen.
    [Fact]
    public void Bleeding_shape_keeps_a_margin_visible_at_the_top()
    {
        var (_, y) = ThemePlacement.ClampPosition(0, -5000, CanvasW, CanvasH, CanvasW, CanvasH, allowBleed: true);
        Assert.Equal(Margin - CanvasH, y, Tol); // -1020: bottom 60px (y+height) remain visible
    }

    // Horizontal bleed works too: a band can sit mostly off the right edge as long as 60px remain.
    [Fact]
    public void Bleeding_shape_keeps_a_margin_visible_on_the_right()
    {
        var (x, _) = ThemePlacement.ClampPosition(5000, 800, 800, 300, CanvasW, CanvasH, allowBleed: true);
        Assert.Equal(CanvasW - Margin, x, Tol); // 1860: left 60px remain visible
    }
}
