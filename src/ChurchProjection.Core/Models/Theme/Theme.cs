namespace ChurchProjection.Core.Models.Theme;

public enum ThemeBackgroundKind
{
    /// <summary>A flat solid colour (house screen). Opaque — blocks any live background media.</summary>
    Solid,

    /// <summary>Chroma-key green for an ATEM keyer to remove.</summary>
    KeyColorGreen,

    /// <summary>Pure black for an ATEM luma keyer to remove.</summary>
    KeyColorBlack,

    /// <summary>A still background image painted behind the text.</summary>
    Image,

    /// <summary>
    /// No background of its own — fully transparent. This is the only kind that lets the operator's
    /// live background (motion graphics / images) show through behind the text. Use it for themes
    /// designed to sit on top of swappable media (e.g. a lower third over a motion loop).
    /// </summary>
    Placeholder,
}

public enum ThemeTextAlign { Left, Center, Right }

public enum ThemeVerticalAlign { Top, Center, Bottom }

public enum ThemeLayout { FullScreen, LowerThird }

/// <summary>How a background image is fitted to the screen.</summary>
public enum ThemeImageFit { Fill, Uniform, UniformToFill }

/// <summary>
/// A decorative shape painted behind the text (accent bars, lower-third panels, color blocks).
/// Positioned in the same 1920x1080 design space as the text regions.
/// </summary>
public sealed class ThemeShape
{
    /// <summary>Operator-given name for this layer (e.g. "Lower-third"). Null = use an auto label
    /// derived from the shape (see <see cref="ThemeLayerNaming.DefaultLabel"/>).</summary>
    public string? Name { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 600;
    public double Height { get; set; } = 120;
    public string Color { get; set; } = "#80FFFFFF";
    public double CornerRadius { get; set; }
    public double Opacity { get; set; } = 1.0;

    /// <summary>Optional image painted inside the shape (over the fill colour). Null = solid fill only.</summary>
    public string? ImagePath { get; set; }

    /// <summary>How the shape image is fitted within the shape bounds.</summary>
    public ThemeImageFit ImageFit { get; set; } = ThemeImageFit.UniformToFill;

    /// <summary>Free pan of the image inside the shape (design px). Lets you slide artwork/video around
    /// within the box; the box clips whatever falls outside.</summary>
    public double ImageOffsetX { get; set; }
    public double ImageOffsetY { get; set; }

    /// <summary>Zoom applied to the image inside the shape (1 = fit per <see cref="ImageFit"/>).</summary>
    public double ImageZoom { get; set; } = 1.0;

    /// <summary>When true the shape is filled with the operator's live background (image or video loop)
    /// instead of a still image — e.g. a lower-third panel that plays a motion clip.</summary>
    public bool UseLiveBackground { get; set; }

    public ThemeShape Clone() => (ThemeShape)MemberwiseClone();
}

/// <summary>
/// A positionable text box within the 1920x1080 design canvas. Coordinates are absolute in that
/// canvas space; the renderer scales the canvas uniformly to the real output resolution. This is
/// what lets a theme place the title/body/footer (ProPresenter-style) anywhere on screen.
/// </summary>
public sealed class ThemeRegion
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Visible { get; set; } = true;

    /// <summary>Fill painted behind this region's text (a "box"). Transparent (#00000000) by default
    /// so text floats unless the user wants a coloured caption box behind it.</summary>
    public string BackgroundColor { get; set; } = "#00000000";

    /// <summary>Rounded-corner radius of the background box.</summary>
    public double BackgroundCornerRadius { get; set; }

    /// <summary>Optional image painted inside the caption box (over the box colour). Null = colour only.
    /// Lets a region's text sit on a custom artwork panel without touching the full-screen background.</summary>
    public string? BackgroundImagePath { get; set; }

    /// <summary>How the caption-box image is fitted within the box bounds.</summary>
    public ThemeImageFit BackgroundImageFit { get; set; } = ThemeImageFit.UniformToFill;

    /// <summary>Free pan of the caption-box image inside the box (design px). The box clips the overflow,
    /// so you can slide the artwork/video to frame it however you like.</summary>
    public double BackgroundImageOffsetX { get; set; }
    public double BackgroundImageOffsetY { get; set; }

    /// <summary>Zoom applied to the caption-box image (1 = fit per <see cref="BackgroundImageFit"/>).</summary>
    public double BackgroundImageZoom { get; set; } = 1.0;

    /// <summary>When true this box is filled with the operator's live background (image or video loop)
    /// instead of a still image — e.g. a lower third whose panel plays a motion clip.</summary>
    public bool UseLiveBackground { get; set; }

    /// <summary>Inner padding (design px) between the box edges and the text, so the text never touches
    /// the sides of its caption box.</summary>
    public double TextPaddingX { get; set; }
    public double TextPaddingY { get; set; }

    /// <summary>Horizontal alignment of the text lines within the box.</summary>
    public ThemeTextAlign HAlign { get; set; } = ThemeTextAlign.Center;

    /// <summary>Vertical alignment of the text block within the box.</summary>
    public ThemeVerticalAlign VAlign { get; set; } = ThemeVerticalAlign.Center;

    /// <summary>When true the text shrinks/grows to fill this box (bounded), so it's never clipped.</summary>
    public bool AutoFit { get; set; }
    public double MinFontSize { get; set; } = 24;
    public double MaxFontSize { get; set; } = 140;

    public ThemeRegion Clone() => (ThemeRegion)MemberwiseClone();
}

/// <summary>
/// A named look for projected output: fonts, colours, background (incl. ATEM key colours),
/// text legibility (outline/shadow) and layout. Colours are stored as hex strings so the
/// model stays serializable and free of any UI-framework types.
/// </summary>
public sealed class Theme
{
    /// <summary>Standard ATEM chroma-key green.</summary>
    public const string KeyGreen = "#FF00B140";

    /// <summary>Luma-key black.</summary>
    public const string KeyBlack = "#FF000000";

    /// <summary>The canonical design canvas the regions are laid out against.</summary>
    public const double CanvasWidth = 1920;
    public const double CanvasHeight = 1080;

    public string Name { get; set; } = "New Theme";

    public string FontFamily { get; set; } = "Segoe UI";
    public double BodyFontSize { get; set; } = 64;
    public double TitleFontSize { get; set; } = 34;
    public double FooterFontSize { get; set; } = 24;
    public bool Bold { get; set; } = true;
    public double LineHeightMultiplier { get; set; } = 1.25;
    public ThemeTextAlign TextAlign { get; set; } = ThemeTextAlign.Center;

    public string TextColor { get; set; } = "#FFFFFFFF";
    public string TitleColor { get; set; } = "#CCFFFFFF";
    public string FooterColor { get; set; } = "#99FFFFFF";

    public double PaddingHorizontal { get; set; } = 60;
    public double PaddingVertical { get; set; } = 40;
    public ThemeLayout Layout { get; set; } = ThemeLayout.FullScreen;

    /// <summary>
    /// Optional per-element layout boxes. Null on older/auto themes — in that case
    /// <see cref="ResolveRegions"/> derives sensible boxes from padding + layout so existing
    /// themes render exactly as before. The Theme Studio sets these explicitly once edited.
    /// </summary>
    public ThemeRegion? TitleRegion { get; set; }
    public ThemeRegion? BodyRegion { get; set; }
    public ThemeRegion? FooterRegion { get; set; }

    public ThemeBackgroundKind BackgroundKind { get; set; } = ThemeBackgroundKind.Solid;
    public string BackgroundColor { get; set; } = "#FF000000";
    public string? BackgroundImagePath { get; set; }
    public ThemeImageFit ImageFit { get; set; } = ThemeImageFit.UniformToFill;

    /// <summary>Colour of the legacy full-width panel drawn behind text in the Lower Third layout.
    /// Transparent by default — new themes use per-region caption boxes instead of this band, so a
    /// fresh lower-third theme no longer starts with a forced black bar.</summary>
    public string LowerThirdBarColor { get; set; } = "#00000000";

    public bool OutlineEnabled { get; set; }
    public string OutlineColor { get; set; } = "#FF000000";
    public double OutlineWidth { get; set; } = 2;

    public bool ShadowEnabled { get; set; } = true;
    public string ShadowColor { get; set; } = "#FF000000";
    public double ShadowBlur { get; set; } = 8;
    public double ShadowOffsetX { get; set; }
    public double ShadowOffsetY { get; set; } = 2;
    public double ShadowOpacity { get; set; } = 0.85;

    /// <summary>Decorative shapes painted behind the text (accent bars, panels).</summary>
    public List<ThemeShape> Shapes { get; set; } = [];

    /// <summary>The colour actually painted as the background, resolving key-colour kinds.</summary>
    public string EffectiveBackgroundColor => BackgroundKind switch
    {
        ThemeBackgroundKind.KeyColorGreen => KeyGreen,
        ThemeBackgroundKind.KeyColorBlack => KeyBlack,
        ThemeBackgroundKind.Placeholder => "#00000000",
        _ => BackgroundColor,
    };

    public Theme Clone()
    {
        var clone = (Theme)MemberwiseClone();
        clone.TitleRegion = TitleRegion?.Clone();
        clone.BodyRegion = BodyRegion?.Clone();
        clone.FooterRegion = FooterRegion?.Clone();
        clone.Shapes = Shapes.Select(s => s.Clone()).ToList();
        return clone;
    }

    /// <summary>
    /// Returns the resolved layout boxes for title/body/footer, deriving defaults from padding and
    /// layout when a region has not been explicitly set. Always returns concrete regions so callers
    /// (renderer, pagination, editor) never have to special-case nulls.
    /// </summary>
    public (ThemeRegion Title, ThemeRegion Body, ThemeRegion Footer) ResolveRegions()
    {
        var padH = PaddingHorizontal;
        var padV = PaddingVertical;
        var contentW = Math.Max(40, CanvasWidth - padH * 2);

        if (Layout == ThemeLayout.LowerThird)
        {
            var ftH = Math.Max(28, FooterFontSize * 1.3 + 10);
            var bandTop = CanvasHeight - 360;
            var footerY = CanvasHeight - padV - ftH;

            var lowerTitle = TitleRegion ?? new ThemeRegion
            {
                X = padH, Y = padV, Width = contentW, Height = Math.Max(40, TitleFontSize * 1.3 + 18),
                Visible = false, HAlign = TextAlign, VAlign = ThemeVerticalAlign.Top,
            };
            var lowerBody = BodyRegion ?? new ThemeRegion
            {
                X = padH, Y = bandTop, Width = contentW, Height = Math.Max(80, footerY - bandTop - 6),
                HAlign = TextAlign, VAlign = ThemeVerticalAlign.Bottom,
            };
            var lowerFooter = FooterRegion ?? new ThemeRegion
            {
                X = padH, Y = footerY, Width = contentW, Height = ftH,
                HAlign = TextAlign, VAlign = ThemeVerticalAlign.Bottom,
            };
            return (lowerTitle, lowerBody, lowerFooter);
        }

        var titleH = Math.Max(40, TitleFontSize * 1.3 + 18);
        var footerH = Math.Max(28, FooterFontSize * 1.3 + 18);

        var title = TitleRegion ?? new ThemeRegion
        {
            X = padH, Y = padV, Width = contentW, Height = titleH,
            HAlign = TextAlign, VAlign = ThemeVerticalAlign.Top,
        };
        var footer = FooterRegion ?? new ThemeRegion
        {
            X = padH, Y = CanvasHeight - padV - footerH, Width = contentW, Height = footerH,
            HAlign = TextAlign, VAlign = ThemeVerticalAlign.Bottom,
        };
        var bodyY = padV + titleH;
        var body = BodyRegion ?? new ThemeRegion
        {
            X = padH, Y = bodyY, Width = contentW,
            Height = Math.Max(120, CanvasHeight - padV - footerH - bodyY),
            HAlign = TextAlign, VAlign = ThemeVerticalAlign.Center,
        };
        return (title, body, footer);
    }
}
