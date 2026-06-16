using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ChurchProjection.UI.Controls;

/// <summary>
/// Renders wrapping text with an optional true outline (stroke) drawn behind the fill, for
/// legibility over video / keyed backgrounds. Avalonia's TextBlock has no stroke, so this builds
/// the glyph geometry via <see cref="FormattedText"/> and strokes it.
/// </summary>
public class OutlinedTextBlock : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, FontFamily>(nameof(FontFamily), FontFamily.Default);

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, double>(nameof(FontSize), 32d);

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, IBrush?>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, TextAlignment>(nameof(TextAlignment), TextAlignment.Center);

    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, double>(nameof(LineHeight), double.NaN);

    public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, IBrush?>(nameof(OutlineBrush));

    public static readonly StyledProperty<double> OutlineThicknessProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, double>(nameof(OutlineThickness), 0d);

    public static readonly StyledProperty<bool> AutoFitProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, bool>(nameof(AutoFit));

    public static readonly StyledProperty<double> MinFontSizeProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, double>(nameof(MinFontSize), 12d);

    public static readonly StyledProperty<double> MaxFontSizeProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, double>(nameof(MaxFontSize), 200d);

    /// <summary>Line height as a multiple of the (effective) font size, used while auto-fitting.</summary>
    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<OutlinedTextBlock, double>(nameof(LineSpacing), 1.2d);

    static OutlinedTextBlock()
    {
        AffectsRender<OutlinedTextBlock>(TextProperty, FontFamilyProperty, FontSizeProperty, FontWeightProperty,
            ForegroundProperty, TextAlignmentProperty, LineHeightProperty, OutlineBrushProperty, OutlineThicknessProperty,
            AutoFitProperty, MinFontSizeProperty, MaxFontSizeProperty, LineSpacingProperty);
        AffectsMeasure<OutlinedTextBlock>(TextProperty, FontFamilyProperty, FontSizeProperty, FontWeightProperty,
            TextAlignmentProperty, LineHeightProperty, AutoFitProperty, MinFontSizeProperty, MaxFontSizeProperty,
            LineSpacingProperty);
    }

    private double _effectiveFontSize = 32d;
    private double _effectiveLineHeight = double.NaN;

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public TextAlignment TextAlignment { get => GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public double LineHeight { get => GetValue(LineHeightProperty); set => SetValue(LineHeightProperty, value); }
    public IBrush? OutlineBrush { get => GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }
    public double OutlineThickness { get => GetValue(OutlineThicknessProperty); set => SetValue(OutlineThicknessProperty, value); }
    public bool AutoFit { get => GetValue(AutoFitProperty); set => SetValue(AutoFitProperty, value); }
    public double MinFontSize { get => GetValue(MinFontSizeProperty); set => SetValue(MinFontSizeProperty, value); }
    public double MaxFontSize { get => GetValue(MaxFontSizeProperty); set => SetValue(MaxFontSizeProperty, value); }
    public double LineSpacing { get => GetValue(LineSpacingProperty); set => SetValue(LineSpacingProperty, value); }

    private FormattedText? CreateFormattedText(double maxWidth, double fontSize, double lineHeight)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text)) return null;

        var typeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight);
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Foreground)
        {
            TextAlignment = TextAlignment,
            Trimming = TextTrimming.None,
        };

        if (!double.IsNaN(maxWidth) && !double.IsInfinity(maxWidth) && maxWidth > 0)
            ft.MaxTextWidth = maxWidth;

        if (!double.IsNaN(lineHeight) && lineHeight > 0)
            ft.LineHeight = lineHeight;

        return ft;
    }

    /// <summary>Binary-searches the largest font size whose text fits within the available box.</summary>
    private double ResolveFontSize(Size available)
    {
        if (!AutoFit) return FontSize;
        if (double.IsInfinity(available.Width) || double.IsInfinity(available.Height) ||
            available.Width <= 0 || available.Height <= 0)
            return FontSize;

        var lo = Math.Max(1, Math.Min(MinFontSize, MaxFontSize));
        var hi = Math.Max(lo, MaxFontSize);

        bool Fits(double size)
        {
            var ft = CreateFormattedText(available.Width, size, size * LineSpacing);
            return ft is null || (ft.Height <= available.Height && ft.Width <= available.Width + 0.5);
        }

        // If even the smallest size overflows, use it anyway (better than nothing).
        if (!Fits(lo)) return lo;

        for (var i = 0; i < 14; i++)
        {
            var mid = (lo + hi) / 2;
            if (Fits(mid)) lo = mid; else hi = mid;
        }
        return lo;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _effectiveFontSize = ResolveFontSize(availableSize);
        _effectiveLineHeight = AutoFit ? _effectiveFontSize * LineSpacing : LineHeight;

        var ft = CreateFormattedText(availableSize.Width, _effectiveFontSize, _effectiveLineHeight);
        if (ft is null) return default;

        var width = double.IsInfinity(availableSize.Width) ? ft.Width : Math.Min(ft.Width, availableSize.Width);
        return new Size(width, ft.Height);
    }

    public override void Render(DrawingContext context)
    {
        var ft = CreateFormattedText(Bounds.Width, _effectiveFontSize, _effectiveLineHeight);
        if (ft is null) return;

        var geometry = ft.BuildGeometry(new Point(0, 0));
        if (geometry is null) return;

        if (OutlineBrush is { } outline && OutlineThickness > 0)
        {
            // Stroke is centred on the glyph edge; double the width so the fill covers the inner half.
            var pen = new Pen(outline, OutlineThickness * 2) { LineJoin = PenLineJoin.Round };
            context.DrawGeometry(null, pen, geometry);
        }

        context.DrawGeometry(Foreground, null, geometry);
    }
}
