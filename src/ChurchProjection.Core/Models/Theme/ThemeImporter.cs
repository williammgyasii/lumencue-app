namespace ChurchProjection.Core.Models.Theme;

/// <summary>
/// Turns a church's designed lower-third graphic (any source size) into a <see cref="Theme"/> we
/// render at 1920x1080 and feed to an ATEM.
///
/// The client designs on whatever canvas they like; our output is always 1920x1080. So the graphic
/// is laid onto the frame at <b>full width, with its native aspect ratio preserved, anchored to the
/// bottom</b> — this is what stops a uniform "contain" fit from shrinking an off-size export into the
/// middle of the frame. A true 16:9 graphic therefore fills the whole frame; an odd-ratio/trimmed
/// one spans the full width as a bottom band.
///
/// The frame's empty area is the ATEM key colour (green) so the switcher composites it over the live
/// camera, and a heading + verse region are seeded on top for the operator to position our text.
/// </summary>
public static class ThemeImporter
{
    public static Theme FromImage(string name, string imagePath, int pixelWidth, int pixelHeight)
    {
        var canvasW = Theme.CanvasWidth;
        var canvasH = Theme.CanvasHeight;

        // Aspect ratio of the source graphic (fall back to the canvas ratio for bad metadata).
        var aspect = pixelWidth > 0 && pixelHeight > 0
            ? (double)pixelWidth / pixelHeight
            : canvasW / canvasH;

        // Fit to full width, preserve proportions.
        var width = canvasW;
        var height = width / aspect;

        // Guard the unusual taller-than-16:9 (portrait) case so it can't overflow the frame.
        if (height > canvasH)
        {
            height = canvasH;
            width = height * aspect;
        }

        var x = (canvasW - width) / 2.0; // centred (0 for the common full-width case)
        var y = canvasH - height;        // bottom-anchored

        var graphic = new ThemeShape
        {
            Name = "Lower-third",
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Color = "#00000000",                  // no fill — the image is the content
            ImagePath = imagePath,
            ImageFit = ThemeImageFit.Fill,         // shape matches the image's ratio, so Fill is exact
            CornerRadius = 0,
            Opacity = 1.0,
        };

        var theme = new Theme
        {
            Name = name,
            Layout = ThemeLayout.LowerThird,
            BackgroundKind = ThemeBackgroundKind.KeyColorGreen,
            ShadowEnabled = true,                  // keep our overlaid text legible on the graphic
            Shapes = { graphic },
        };

        SeedTextRegions(theme, graphic);
        return theme;
    }

    // Seeds a heading (Title), verse (Body) and reference (Footer) inside the graphic's band so the
    // operator has movable/resizable boxes to drop straight onto the design in Theme Studio.
    private static void SeedTextRegions(Theme theme, ThemeShape graphic)
    {
        const double padX = 120;

        // Work within the graphic's vertical band, but never above the lower third of the frame so a
        // full-frame graphic still gets its text down where a lower third belongs.
        var bandTop = Math.Max(graphic.Y, Theme.CanvasHeight - 360);
        var bandBottom = graphic.Y + graphic.Height;
        var bandHeight = Math.Max(120, bandBottom - bandTop);

        var headingH = Math.Max(40, bandHeight * 0.22);
        var footerH = Math.Max(32, bandHeight * 0.20);
        var bodyH = Math.Max(60, bandHeight - headingH - footerH);

        var contentW = Theme.CanvasWidth - padX * 2;

        theme.TitleRegion = new ThemeRegion
        {
            X = padX, Y = bandTop, Width = contentW, Height = headingH,
            Visible = true, HAlign = ThemeTextAlign.Center, VAlign = ThemeVerticalAlign.Center,
        };
        theme.TitleRegion.ApplyDefaultContentBindings(ThemeTextSlot.Title);

        theme.BodyRegion = new ThemeRegion
        {
            X = padX, Y = bandTop + headingH, Width = contentW, Height = bodyH,
            Visible = true, HAlign = ThemeTextAlign.Center, VAlign = ThemeVerticalAlign.Center,
            AutoFit = true,
        };
        theme.BodyRegion.ApplyDefaultContentBindings(ThemeTextSlot.Body);

        theme.FooterRegion = new ThemeRegion
        {
            X = padX, Y = bandTop + headingH + bodyH, Width = contentW, Height = footerH,
            Visible = true, HAlign = ThemeTextAlign.Center, VAlign = ThemeVerticalAlign.Center,
        };
        theme.FooterRegion.ApplyDefaultContentBindings(ThemeTextSlot.Footer);

        theme.UsesLayerEditor = true;
    }
}
