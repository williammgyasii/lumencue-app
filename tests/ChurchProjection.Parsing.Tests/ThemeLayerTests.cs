using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Theme;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ThemeLayerTests
{
    [Fact]
    public void ResolveActiveRegions_LegacyTheme_DerivesAllThreeSlots()
    {
        var theme = new Theme();

        var (title, body, footer) = theme.ResolveActiveRegions();

        Assert.NotNull(title);
        Assert.NotNull(body);
        Assert.NotNull(footer);
    }

    [Fact]
    public void ResolveActiveRegions_ExplicitEditor_RespectsDeletedSlots()
    {
        var theme = new Theme
        {
            UsesLayerEditor = true,
            TitleRegion = null,
            BodyRegion = new ThemeRegion { Width = 800, Height = 400 },
            FooterRegion = null,
        };

        var (title, body, footer) = theme.ResolveActiveRegions();

        Assert.Null(title);
        Assert.NotNull(body);
        Assert.Null(footer);
    }

    [Fact]
    public void EnsureEditorRegions_DoesNotResurrectAnEmptySavedLayout()
    {
        var theme = new Theme
        {
            UsesLayerEditor = true,
            TitleRegion = null,
            BodyRegion = null,
            FooterRegion = null,
        };

        theme.EnsureEditorRegions();

        Assert.Null(theme.TitleRegion);
        Assert.Null(theme.BodyRegion);
        Assert.Null(theme.FooterRegion);
    }

    [Fact]
    public void EnsureEditorRegions_MaterializesLegacyThemesThatNeverHadLayers()
    {
        var theme = new Theme { UsesLayerEditor = false };

        theme.EnsureEditorRegions();

        Assert.NotNull(theme.TitleRegion);
        Assert.NotNull(theme.BodyRegion);
        Assert.NotNull(theme.FooterRegion);
    }

    [Fact]
    public void ThemeJson_RoundTripsAThemeWithEveryTextLayerDeleted()
    {
        var theme = new Theme
        {
            Name = "Empty layout",
            UsesLayerEditor = true,
            TitleRegion = null,
            BodyRegion = null,
            FooterRegion = null,
            BackgroundKind = ThemeBackgroundKind.Placeholder,
        };
        var options = new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        var loaded = System.Text.Json.JsonSerializer.Deserialize<Theme>(
            System.Text.Json.JsonSerializer.Serialize(theme, options), options);

        Assert.NotNull(loaded);
        Assert.True(loaded!.UsesLayerEditor);
        Assert.Null(loaded.TitleRegion);
        Assert.Null(loaded.BodyRegion);
        Assert.Null(loaded.FooterRegion);
        Assert.Equal(ThemeBackgroundKind.Placeholder, loaded.BackgroundKind);
    }

    [Fact]
    public void ResolvePaginationRegion_PrefersLayerBoundToBody()
    {
        var theme = new Theme
        {
            UsesLayerEditor = true,
            TitleRegion = new ThemeRegion
            {
                Width = 600,
                Height = 80,
                ScriptureField = ThemeContentField.Title,
                SongField = ThemeContentField.Title,
            },
            BodyRegion = new ThemeRegion
            {
                Width = 600,
                Height = 400,
                AutoFit = true,
                MinFontSize = 28,
                ScriptureField = ThemeContentField.Body,
                SongField = ThemeContentField.Body,
            },
        };

        var region = theme.ResolvePaginationRegion(SlideType.Scripture);

        Assert.Same(theme.BodyRegion, region);
        Assert.True(region.AutoFit);
    }

    [Fact]
    public void ThemeRegion_DefaultBindings_RouteScriptureReferenceToFooter()
    {
        var footer = new ThemeRegion();
        footer.ApplyDefaultContentBindings(ThemeTextSlot.Footer);

        Assert.Equal(ThemeContentField.Footer, footer.GetContentField(SlideType.Scripture));
        Assert.Equal(ThemeContentField.None, footer.GetContentField(SlideType.Lyric));
    }

    [Fact]
    public void ThemeRegion_ResolveSlideText_MapsFields()
    {
        Assert.Equal("Ref", ThemeRegion.ResolveSlideText("Ref", "Verse", "Foot", ThemeContentField.Title));
        Assert.Equal("Verse", ThemeRegion.ResolveSlideText("Ref", "Verse", "Foot", ThemeContentField.Body));
        Assert.Equal("Foot", ThemeRegion.ResolveSlideText("Ref", "Verse", "Foot", ThemeContentField.Footer));
        Assert.Equal("", ThemeRegion.ResolveSlideText("Ref", "Verse", "Foot", ThemeContentField.None));
    }

    [Fact]
    public void StudioBackground_OffersSolidImagePlaceholder_AndMapsKeyColorsToSolid()
    {
        Assert.Equal(
            [ThemeBackgroundKind.Solid, ThemeBackgroundKind.Image, ThemeBackgroundKind.Placeholder],
            ThemeBackgroundStudio.EditorTypes);

        Assert.Equal(ThemeBackgroundKind.Solid, ThemeBackgroundStudio.ForEditor(ThemeBackgroundKind.KeyColorGreen));
        Assert.Equal(ThemeBackgroundKind.Solid, ThemeBackgroundStudio.ForEditor(ThemeBackgroundKind.KeyColorBlack));
        Assert.Equal(ThemeBackgroundKind.Image, ThemeBackgroundStudio.ForEditor(ThemeBackgroundKind.Image));

        Assert.True(ThemeBackgroundStudio.ShowsColorPicker(ThemeBackgroundKind.Solid));
        Assert.True(ThemeBackgroundStudio.ShowsColorPicker(ThemeBackgroundKind.KeyColorGreen));
        Assert.False(ThemeBackgroundStudio.ShowsColorPicker(ThemeBackgroundKind.Image));
        Assert.False(ThemeBackgroundStudio.ShowsColorPicker(ThemeBackgroundKind.Placeholder));
        Assert.True(ThemeBackgroundStudio.ShowsImagePicker(ThemeBackgroundKind.Image));
        Assert.False(ThemeBackgroundStudio.ShowsImagePicker(ThemeBackgroundKind.Solid));

        var keyed = new Theme { BackgroundKind = ThemeBackgroundKind.KeyColorGreen, BackgroundColor = "#FF111111" };
        Assert.Equal(Theme.KeyGreen, ThemeBackgroundStudio.EditorColor(keyed));
    }

    [Fact]
    public void ApplyEditorColor_DoesNotForceSolidOverImageOrPlaceholder()
    {
        var image = new Theme
        {
            BackgroundKind = ThemeBackgroundKind.Image,
            BackgroundColor = "#FF101418",
            BackgroundImagePath = "/tmp/bg.png",
        };

        ThemeBackgroundStudio.ApplyEditorColor(image, "#FF101418");

        Assert.Equal(ThemeBackgroundKind.Image, image.BackgroundKind);
        Assert.Equal("#FF101418", image.BackgroundColor);

        var placeholder = new Theme { BackgroundKind = ThemeBackgroundKind.Placeholder, BackgroundColor = "#FF101418" };
        ThemeBackgroundStudio.ApplyEditorColor(placeholder, "#FF223344");

        Assert.Equal(ThemeBackgroundKind.Placeholder, placeholder.BackgroundKind);
        Assert.Equal("#FF223344", placeholder.BackgroundColor);
    }

    [Fact]
    public void ApplyEditorColor_PromotesKeyColorToSolid()
    {
        var keyed = new Theme { BackgroundKind = ThemeBackgroundKind.KeyColorGreen, BackgroundColor = "#FF111111" };

        ThemeBackgroundStudio.ApplyEditorColor(keyed, "#FFABCDEF");

        Assert.Equal(ThemeBackgroundKind.Solid, keyed.BackgroundKind);
        Assert.Equal("#FFABCDEF", keyed.BackgroundColor);
    }

    [Fact]
    public void ResolvePaint_PlaceholderUsesLiveMediaWhenSelected_OtherwiseStandIn()
    {
        Assert.Equal(
            ThemeBackgroundPaint.LiveMedia,
            ThemeBackgroundResolve.Choose(ThemeBackgroundKind.Placeholder, hasLiveFrame: true, hasThemeImage: false));
        Assert.Equal(
            ThemeBackgroundPaint.PlaceholderStandIn,
            ThemeBackgroundResolve.Choose(ThemeBackgroundKind.Placeholder, hasLiveFrame: false, hasThemeImage: false));
    }

    [Fact]
    public void AcceptsLiveSelection_OnlyPlaceholderThemes()
    {
        Assert.True(ThemeBackgroundResolve.AcceptsLiveSelection(ThemeBackgroundKind.Placeholder));
        Assert.False(ThemeBackgroundResolve.AcceptsLiveSelection(ThemeBackgroundKind.Solid));
        Assert.False(ThemeBackgroundResolve.AcceptsLiveSelection(ThemeBackgroundKind.Image));
        Assert.False(ThemeBackgroundResolve.AcceptsLiveSelection(ThemeBackgroundKind.KeyColorGreen));
        Assert.False(ThemeBackgroundResolve.AcceptsLiveSelection(ThemeBackgroundKind.KeyColorBlack));
    }

    [Fact]
    public void ResolvePaint_ImageAndSolidIgnoreLiveMedia()
    {
        Assert.Equal(
            ThemeBackgroundPaint.ThemeImage,
            ThemeBackgroundResolve.Choose(ThemeBackgroundKind.Image, hasLiveFrame: true, hasThemeImage: true));
        Assert.Equal(
            ThemeBackgroundPaint.SolidOrKey,
            ThemeBackgroundResolve.Choose(ThemeBackgroundKind.Solid, hasLiveFrame: true, hasThemeImage: false));
        Assert.Equal(
            ThemeBackgroundPaint.SolidOrKey,
            ThemeBackgroundResolve.Choose(ThemeBackgroundKind.KeyColorGreen, hasLiveFrame: true, hasThemeImage: false));
    }

    [Fact]
    public void EffectiveBackgroundColor_ImageAndPlaceholderAreTransparentSoCanvasLeavesSolid()
    {
        var image = new Theme
        {
            BackgroundKind = ThemeBackgroundKind.Image,
            BackgroundColor = "#FF101418",
        };
        var placeholder = new Theme
        {
            BackgroundKind = ThemeBackgroundKind.Placeholder,
            BackgroundColor = "#FF101418",
        };
        var solid = new Theme
        {
            BackgroundKind = ThemeBackgroundKind.Solid,
            BackgroundColor = "#FF101418",
        };

        Assert.Equal("#00000000", image.EffectiveBackgroundColor);
        Assert.Equal("#00000000", placeholder.EffectiveBackgroundColor);
        Assert.Equal("#FF101418", solid.EffectiveBackgroundColor);
    }

    [Fact]
    public void ResolveFonts_FooterAndTitleFallBackToBodyWhenUnset()
    {
        var theme = new Theme { FontFamily = "Georgia" };

        Assert.Equal("Georgia", theme.ResolveBodyFont());
        Assert.Equal("Georgia", theme.ResolveTitleFont());
        Assert.Equal("Georgia", theme.ResolveFooterFont());
    }

    [Fact]
    public void ResolveFonts_FooterAndTitleCanDifferFromBody()
    {
        var theme = new Theme
        {
            FontFamily = "Georgia",
            TitleFontFamily = "Impact",
            FooterFontFamily = "Courier New",
        };

        Assert.Equal("Georgia", theme.ResolveBodyFont());
        Assert.Equal("Impact", theme.ResolveTitleFont());
        Assert.Equal("Courier New", theme.ResolveFooterFont());
    }

    [Fact]
    public void ImportedTheme_SeedsLayerEditorAndBindings()
    {
        var theme = ThemeImporter.FromImage("Test", "C:/art/lt.png", 1920, 1080);

        Assert.True(theme.UsesLayerEditor);
        Assert.Equal(ThemeContentField.Title, theme.TitleRegion!.GetContentField(SlideType.Scripture));
        Assert.Equal(ThemeContentField.Body, theme.BodyRegion!.GetContentField(SlideType.Lyric));
        Assert.Equal(ThemeContentField.Footer, theme.FooterRegion!.GetContentField(SlideType.Scripture));
    }
}
