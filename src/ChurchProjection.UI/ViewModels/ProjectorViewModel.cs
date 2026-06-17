using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ChurchProjection.Core.Models.Projection;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Theme;
using ChurchProjection.Core.Services;
using ChurchProjection.UI.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels;

public class ProjectorViewModel : ViewModelBase, IActivatableViewModel, IDisposable
{
    private readonly IThemeService _themes;
    private readonly CompositeDisposable _subscriptions = new();

    private string _slideTitle = string.Empty;
    private string _slideBody = string.Empty;
    private string _slideFooter = string.Empty;
    private bool _isBlank = true;
    private SlideType _currentSlideType = SlideType.Blank;

    // Self-ticking live elements (countdown / clock) update their body text once a second
    // without re-projecting, so there is no fade/flicker between ticks.
    private DispatcherTimer? _liveTimer;
    private DateTime? _countdownTargetUtc;
    private string _countdownDoneMessage = string.Empty;
    private string? _clockFormat;

    private FontFamily _fontFamily = FontFamily.Default;
    private double _bodyFontSize = 64;
    private double _titleFontSize = 34;
    private double _footerFontSize = 24;
    private double _bodyLineHeight = 80;
    private FontWeight _fontWeight = FontWeight.Bold;
    private TextAlignment _textAlignment = TextAlignment.Center;

    private IBrush _backgroundBrush = Brushes.Black;
    private Bitmap? _backgroundImage;
    private bool _hasBackgroundImage;
    private Stretch _backgroundImageStretch = Stretch.UniformToFill;
    private IBrush _lowerThirdBarBrush = Brushes.Transparent;

    private IBrush _textBrush = Brushes.White;
    private IBrush _titleBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private IBrush _footerBrush = new SolidColorBrush(Color.Parse("#99FFFFFF"));

    private IBrush? _outlineBrush;
    private double _outlineThickness;
    private IEffect? _textShadow;

    private bool _isLowerThird;
    private Thickness _contentPadding = new(60, 40);

    private double _barX, _barY, _barWidth, _barHeight;
    private bool _lowerThirdBarVisible;

    // Background layering: the theme's own background image vs. the operator's swappable live layer.
    private Bitmap? _themeBackgroundImage;
    private bool _themeIsPlaceholder;
    private Bitmap? _liveFrame;

    // Announcement layer: a full graphic / lower-third / video painted on top of everything.
    private Bitmap? _announcementImage;

    // Compositing layer state (driven per channel by ILayerService): enable + opacity for every layer,
    // plus the persistent Overlay logo and the Alert banner this output owns.
    private bool _backgroundLayerEnabled = true;
    private double _backgroundLayerOpacity = 1;
    private bool _slideLayerEnabled = true;
    private double _slideLayerOpacity = 1;
    private bool _mediaLayerEnabled = true;
    private double _mediaLayerOpacity = 1;
    private bool _overlayLayerEnabled = true;
    private double _overlayLayerOpacity = 1;
    private bool _alertLayerEnabled = true;
    private double _alertLayerOpacity = 1;

    private string? _overlayImagePath;
    private Bitmap? _overlayImage;
    private double _overlayWidth = 1920 * 0.18;
    private HorizontalAlignment _overlayHAlign = HorizontalAlignment.Right;
    private VerticalAlignment _overlayVAlign = VerticalAlignment.Top;
    private string _alertText = string.Empty;

    public ViewModelActivator Activator { get; } = new();

    /// <summary>The fixed design canvas the regions are positioned within; scaled to the real output.</summary>
    public double CanvasWidth => Theme.CanvasWidth;
    public double CanvasHeight => Theme.CanvasHeight;

    /// <summary>Positioned text boxes (driven by the theme's regions).</summary>
    public RegionVm TitleRegion { get; } = new();
    public RegionVm BodyRegion { get; } = new();
    public RegionVm FooterRegion { get; } = new();

    /// <summary>Decorative shapes painted behind the text.</summary>
    public ObservableCollection<ShapeVm> Shapes { get; } = [];

    private string? _themeOverrideName;

    /// <summary>The live announcement media painted on top of the output (still graphic or video frame).</summary>
    public Bitmap? AnnouncementImage
    {
        get => _announcementImage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _announcementImage, value);
            this.RaisePropertyChanged(nameof(ShowAnnouncement));
        }
    }

    /// <summary>True while an announcement is live, the Media layer is enabled, and it should cover the screen.</summary>
    public bool ShowAnnouncement => _announcementImage is not null && _mediaLayerEnabled;

    // ----- Compositing layers (per channel) -----

    /// <summary>Background layer (theme colour + image/motion) enable + opacity.</summary>
    public bool BackgroundLayerEnabled { get => _backgroundLayerEnabled; private set => this.RaiseAndSetIfChanged(ref _backgroundLayerEnabled, value); }
    public double BackgroundLayerOpacity { get => _backgroundLayerOpacity; private set => this.RaiseAndSetIfChanged(ref _backgroundLayerOpacity, value); }

    /// <summary>Slide/text layer opacity (visibility is also gated by <see cref="IsBlank"/> via <see cref="ShowSlide"/>).</summary>
    public double SlideLayerOpacity { get => _slideLayerOpacity; private set => this.RaiseAndSetIfChanged(ref _slideLayerOpacity, value); }
    public bool SlideLayerEnabled
    {
        get => _slideLayerEnabled;
        private set { this.RaiseAndSetIfChanged(ref _slideLayerEnabled, value); this.RaisePropertyChanged(nameof(ShowSlide)); }
    }

    /// <summary>True when the themed text content should paint (not blank and the Slide layer is on).</summary>
    public bool ShowSlide => !_isBlank && _slideLayerEnabled;

    /// <summary>Media layer opacity (visibility gated by a live clip via <see cref="ShowAnnouncement"/>).</summary>
    public double MediaLayerOpacity { get => _mediaLayerOpacity; private set => this.RaiseAndSetIfChanged(ref _mediaLayerOpacity, value); }
    public bool MediaLayerEnabled
    {
        get => _mediaLayerEnabled;
        private set { this.RaiseAndSetIfChanged(ref _mediaLayerEnabled, value); this.RaisePropertyChanged(nameof(ShowAnnouncement)); }
    }

    /// <summary>The persistent overlay logo/watermark, its geometry and the layer's opacity.</summary>
    public Bitmap? OverlayImage { get => _overlayImage; private set { this.RaiseAndSetIfChanged(ref _overlayImage, value); this.RaisePropertyChanged(nameof(ShowOverlay)); } }
    public double OverlayWidth { get => _overlayWidth; private set => this.RaiseAndSetIfChanged(ref _overlayWidth, value); }
    public HorizontalAlignment OverlayHAlign { get => _overlayHAlign; private set => this.RaiseAndSetIfChanged(ref _overlayHAlign, value); }
    public VerticalAlignment OverlayVAlign { get => _overlayVAlign; private set => this.RaiseAndSetIfChanged(ref _overlayVAlign, value); }
    public double OverlayLayerOpacity { get => _overlayLayerOpacity; private set => this.RaiseAndSetIfChanged(ref _overlayLayerOpacity, value); }
    public bool OverlayLayerEnabled
    {
        get => _overlayLayerEnabled;
        private set { this.RaiseAndSetIfChanged(ref _overlayLayerEnabled, value); this.RaisePropertyChanged(nameof(ShowOverlay)); }
    }
    public bool ShowOverlay => _overlayLayerEnabled && _overlayImage is not null;

    /// <summary>The alert banner punched over everything, and the layer's opacity.</summary>
    public string AlertText { get => _alertText; private set { this.RaiseAndSetIfChanged(ref _alertText, value); this.RaisePropertyChanged(nameof(ShowAlert)); } }
    public double AlertLayerOpacity { get => _alertLayerOpacity; private set => this.RaiseAndSetIfChanged(ref _alertLayerOpacity, value); }
    public bool AlertLayerEnabled
    {
        get => _alertLayerEnabled;
        private set { this.RaiseAndSetIfChanged(ref _alertLayerEnabled, value); this.RaisePropertyChanged(nameof(ShowAlert)); }
    }
    public bool ShowAlert => _alertLayerEnabled && !string.IsNullOrEmpty(_alertText);

    public ProjectorViewModel(IProjectionService projectionService, IThemeService themes,
        string? themeOverride = null, ILiveBackgroundService? liveBackground = null,
        IAnnouncementService? announcements = null, string? screenKey = null,
        ILayerService? layers = null)
    {
        _themes = themes;
        _themeOverrideName = themeOverride;

        var key = screenKey ?? MediaTarget.AllScreens;

        announcements?.FrameFor(key)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(frame => AnnouncementImage = frame)
            .DisposeWith(_subscriptions);

        layers?.SnapshotFor(key)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ApplyLayers)
            .DisposeWith(_subscriptions);

        liveBackground?.Frame
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(frame =>
            {
                _liveFrame = frame;
                TitleRegion.SetLiveFrame(frame);
                BodyRegion.SetLiveFrame(frame);
                FooterRegion.SetLiveFrame(frame);
                foreach (var shape in Shapes) shape.SetLiveFrame(frame);
                RefreshBackground();
            })
            .DisposeWith(_subscriptions);

        projectionService.CurrentSlide
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(slide =>
            {
                IsBlank = slide.Type == SlideType.Blank;
                SlideTitle = slide.Title;
                SlideBody = slide.Body;
                SlideFooter = slide.Footer;
                _currentSlideType = slide.Type;
                ApplyTheme(ResolveTheme(slide.Type));
                ConfigureLiveClock(slide);
            })
            .DisposeWith(_subscriptions);

        _themes.Changed
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => ApplyTheme(ResolveTheme(_currentSlideType)))
            .DisposeWith(_subscriptions);

        ApplyTheme(ResolveTheme(SlideType.Blank));
    }

    /// <summary>
    /// Forces this output to render a specific theme (e.g. a green-screen lower-third for an ATEM
    /// keyer) regardless of content type. Pass null to fall back to the global per-content assignment.
    /// </summary>
    public void SetThemeOverride(string? themeName)
    {
        _themeOverrideName = themeName;
        ApplyTheme(ResolveTheme(_currentSlideType));
    }

    /// <summary>Resolves the theme for a slide type, honouring this output's override when set.</summary>
    private Theme ResolveTheme(SlideType type)
    {
        if (!string.IsNullOrEmpty(_themeOverrideName))
        {
            var forced = _themes.GetByName(_themeOverrideName);
            if (forced is not null) return forced;
        }
        return _themes.ResolveFor(type);
    }

    /// <summary>Sets static sample text (used by the Theme Studio live preview).</summary>
    public void SetSampleContent(string title, string body, string footer)
    {
        SlideTitle = title;
        SlideBody = body;
        SlideFooter = footer;
        IsBlank = false;
    }

    /// <summary>
    /// Starts/stops the per-second timer for self-updating Countdown and Clock slides.
    /// Other slide types simply render their static body text.
    /// </summary>
    private void ConfigureLiveClock(Slide slide)
    {
        _countdownTargetUtc = slide.CountdownTargetUtc;
        _countdownDoneMessage = slide.Footer;
        _clockFormat = slide.ClockFormat;

        if (slide.Type is SlideType.Countdown or SlideType.Clock)
        {
            // The footer is repurposed as the "done" message on countdowns; clear the visible footer.
            if (slide.Type == SlideType.Countdown)
                SlideFooter = string.Empty;

            TickLiveClock();
            if (_liveTimer is null)
            {
                _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _liveTimer.Tick += (_, _) => TickLiveClock();
            }
            _liveTimer.Start();
        }
        else
        {
            _liveTimer?.Stop();
        }
    }

    private void TickLiveClock()
    {
        if (_currentSlideType == SlideType.Clock)
        {
            SlideBody = DateTime.Now.ToString(string.IsNullOrWhiteSpace(_clockFormat) ? "h:mm tt" : _clockFormat);
            return;
        }

        if (_currentSlideType == SlideType.Countdown && _countdownTargetUtc is { } target)
        {
            var remaining = target - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _liveTimer?.Stop();
                SlideBody = string.IsNullOrWhiteSpace(_countdownDoneMessage) ? "0:00" : _countdownDoneMessage;
                return;
            }

            SlideBody = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
                : $"{remaining.Minutes}:{remaining.Seconds:00}";
        }
    }

    public void Dispose()
    {
        _liveTimer?.Stop();
        _subscriptions.Dispose();

        // Detach bitmaps from the (closing) view's Image controls before disposing, then retire them
        // after the next render commit so the compositor never paints a freed surface. The live-frame
        // buffers belong to the shared background player, so we only drop the reference here.
        var overlay = _overlayImage;
        var themeBg = _themeBackgroundImage;
        _overlayImage = null;
        _themeBackgroundImage = null;
        OverlayImage = null;
        BackgroundImage = null;
        HasBackgroundImage = false;
        SafeBitmapDisposal.Retire(overlay);
        SafeBitmapDisposal.Retire(themeBg);
    }

    public string SlideTitle { get => _slideTitle; set => this.RaiseAndSetIfChanged(ref _slideTitle, value); }
    public string SlideBody { get => _slideBody; set => this.RaiseAndSetIfChanged(ref _slideBody, value); }
    public string SlideFooter { get => _slideFooter; set => this.RaiseAndSetIfChanged(ref _slideFooter, value); }

    public bool IsBlank
    {
        get => _isBlank;
        set { this.RaiseAndSetIfChanged(ref _isBlank, value); this.RaisePropertyChanged(nameof(ShowSlide)); }
    }

    public FontFamily FontFamily { get => _fontFamily; set => this.RaiseAndSetIfChanged(ref _fontFamily, value); }
    public double BodyFontSize { get => _bodyFontSize; set => this.RaiseAndSetIfChanged(ref _bodyFontSize, value); }
    public double TitleFontSize { get => _titleFontSize; set => this.RaiseAndSetIfChanged(ref _titleFontSize, value); }
    public double FooterFontSize { get => _footerFontSize; set => this.RaiseAndSetIfChanged(ref _footerFontSize, value); }
    public double BodyLineHeight { get => _bodyLineHeight; set => this.RaiseAndSetIfChanged(ref _bodyLineHeight, value); }
    public FontWeight FontWeight { get => _fontWeight; set => this.RaiseAndSetIfChanged(ref _fontWeight, value); }
    public TextAlignment TextAlignment { get => _textAlignment; set => this.RaiseAndSetIfChanged(ref _textAlignment, value); }

    public IBrush BackgroundBrush { get => _backgroundBrush; set => this.RaiseAndSetIfChanged(ref _backgroundBrush, value); }
    public Bitmap? BackgroundImage { get => _backgroundImage; set => this.RaiseAndSetIfChanged(ref _backgroundImage, value); }
    public bool HasBackgroundImage { get => _hasBackgroundImage; set => this.RaiseAndSetIfChanged(ref _hasBackgroundImage, value); }
    public Stretch BackgroundImageStretch { get => _backgroundImageStretch; set => this.RaiseAndSetIfChanged(ref _backgroundImageStretch, value); }
    public IBrush LowerThirdBarBrush { get => _lowerThirdBarBrush; set => this.RaiseAndSetIfChanged(ref _lowerThirdBarBrush, value); }

    public IBrush TextBrush { get => _textBrush; set => this.RaiseAndSetIfChanged(ref _textBrush, value); }
    public IBrush TitleBrush { get => _titleBrush; set => this.RaiseAndSetIfChanged(ref _titleBrush, value); }
    public IBrush FooterBrush { get => _footerBrush; set => this.RaiseAndSetIfChanged(ref _footerBrush, value); }

    public IBrush? OutlineBrush { get => _outlineBrush; set => this.RaiseAndSetIfChanged(ref _outlineBrush, value); }
    public double OutlineThickness { get => _outlineThickness; set => this.RaiseAndSetIfChanged(ref _outlineThickness, value); }
    public IEffect? TextShadow { get => _textShadow; set => this.RaiseAndSetIfChanged(ref _textShadow, value); }

    public bool IsLowerThird
    {
        get => _isLowerThird;
        set
        {
            this.RaiseAndSetIfChanged(ref _isLowerThird, value);
            this.RaisePropertyChanged(nameof(IsFullScreen));
        }
    }

    public bool IsFullScreen => !IsLowerThird;

    public Thickness ContentPadding { get => _contentPadding; set => this.RaiseAndSetIfChanged(ref _contentPadding, value); }

    // Lower-third background bar geometry (drawn behind the body/footer band).
    public bool LowerThirdBarVisible { get => _lowerThirdBarVisible; set => this.RaiseAndSetIfChanged(ref _lowerThirdBarVisible, value); }
    public double BarX { get => _barX; set => this.RaiseAndSetIfChanged(ref _barX, value); }
    public double BarY { get => _barY; set => this.RaiseAndSetIfChanged(ref _barY, value); }
    public double BarWidth { get => _barWidth; set => this.RaiseAndSetIfChanged(ref _barWidth, value); }
    public double BarHeight { get => _barHeight; set => this.RaiseAndSetIfChanged(ref _barHeight, value); }

    /// <summary>Applies a theme to a copy held by the preview window (used by the Theme Studio live preview).</summary>
    public void PreviewTheme(Theme theme) => ApplyTheme(theme);

    private void ApplyTheme(Theme t)
    {
        FontFamily = ResolveFontFamily(t.FontFamily);
        BodyFontSize = t.BodyFontSize;
        TitleFontSize = t.TitleFontSize;
        FooterFontSize = t.FooterFontSize;
        BodyLineHeight = t.BodyFontSize * t.LineHeightMultiplier;
        FontWeight = t.Bold ? FontWeight.Bold : FontWeight.Normal;
        TextAlignment = t.TextAlign switch
        {
            ThemeTextAlign.Left => TextAlignment.Left,
            ThemeTextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Center,
        };

        TextBrush = ParseBrush(t.TextColor, Brushes.White);
        TitleBrush = ParseBrush(t.TitleColor, Brushes.White);
        FooterBrush = ParseBrush(t.FooterColor, Brushes.Gray);
        BackgroundBrush = ParseBrush(t.EffectiveBackgroundColor, Brushes.Black);

        _themeIsPlaceholder = t.BackgroundKind == ThemeBackgroundKind.Placeholder;
        LoadBackgroundImage(t);
        RefreshBackground();

        if (t.OutlineEnabled)
        {
            OutlineBrush = ParseBrush(t.OutlineColor, Brushes.Black);
            OutlineThickness = t.OutlineWidth;
        }
        else
        {
            OutlineBrush = null;
            OutlineThickness = 0;
        }

        TextShadow = t.ShadowEnabled
            ? new DropShadowEffect
            {
                BlurRadius = t.ShadowBlur,
                OffsetX = t.ShadowOffsetX,
                OffsetY = t.ShadowOffsetY,
                Color = ParseColor(t.ShadowColor, Colors.Black),
                Opacity = t.ShadowOpacity,
            }
            : null;

        BackgroundImageStretch = t.ImageFit switch
        {
            ThemeImageFit.Fill => Stretch.Fill,
            ThemeImageFit.Uniform => Stretch.Uniform,
            _ => Stretch.UniformToFill,
        };
        LowerThirdBarBrush = ParseBrush(t.LowerThirdBarColor, Brushes.Transparent);

        Shapes.Clear();
        foreach (var s in t.Shapes)
        {
            var shapeVm = new ShapeVm(s);
            shapeVm.SetLiveFrame(_liveFrame);
            Shapes.Add(shapeVm);
        }

        ContentPadding = new Thickness(t.PaddingHorizontal, t.PaddingVertical);
        IsLowerThird = t.Layout == ThemeLayout.LowerThird;

        var (title, body, footer) = t.ResolveRegions();
        TitleRegion.Apply(title, 1.1);
        BodyRegion.Apply(body, t.LineHeightMultiplier);
        FooterRegion.Apply(footer, 1.1);
        TitleRegion.SetLiveFrame(_liveFrame);
        BodyRegion.SetLiveFrame(_liveFrame);
        FooterRegion.SetLiveFrame(_liveFrame);

        // Lower-third bar covers the body + footer band, full width.
        LowerThirdBarVisible = t.Layout == ThemeLayout.LowerThird;
        var bandTop = Math.Min(body.Y, footer.Y) - 16;
        BarX = 0;
        BarY = Math.Max(0, bandTop);
        BarWidth = Theme.CanvasWidth;
        BarHeight = Math.Max(0, Theme.CanvasHeight - BarY);
    }

    private void LoadBackgroundImage(Theme t)
    {
        if (t.BackgroundKind == ThemeBackgroundKind.Image && !string.IsNullOrWhiteSpace(t.BackgroundImagePath) && File.Exists(t.BackgroundImagePath))
        {
            try
            {
                _themeBackgroundImage = new Bitmap(t.BackgroundImagePath);
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load background image {Path}", t.BackgroundImagePath);
            }
        }

        _themeBackgroundImage = null;
    }

    /// <summary>
    /// Chooses the painted background. The operator's live media layer only shows on a
    /// <see cref="ThemeBackgroundKind.Placeholder"/> (transparent) theme that has opted in to it;
    /// every other kind (solid, image, ATEM key colours) keeps its own background and ignores the
    /// live layer, so a black theme stays black and a green key stays clean.
    /// </summary>
    private void RefreshBackground()
    {
        if (_liveFrame is not null && _themeIsPlaceholder)
        {
            BackgroundImage = _liveFrame;
            HasBackgroundImage = true;
        }
        else
        {
            BackgroundImage = _themeBackgroundImage;
            HasBackgroundImage = _themeBackgroundImage is not null;
        }
    }

    /// <summary>Applies a per-channel layer snapshot: enable/opacity for every layer plus the overlay logo
    /// and alert banner. The overlay bitmap is only (re)loaded when its path actually changes.</summary>
    private void ApplyLayers(LayerSnapshot s)
    {
        BackgroundLayerEnabled = s.BackgroundEnabled;
        BackgroundLayerOpacity = s.BackgroundOpacity;
        SlideLayerEnabled = s.SlideEnabled;
        SlideLayerOpacity = s.SlideOpacity;
        MediaLayerEnabled = s.MediaEnabled;
        MediaLayerOpacity = s.MediaOpacity;
        OverlayLayerEnabled = s.OverlayEnabled;
        OverlayLayerOpacity = s.OverlayOpacity;
        AlertLayerEnabled = s.AlertEnabled;
        AlertLayerOpacity = s.AlertOpacity;

        OverlayWidth = Theme.CanvasWidth * Math.Clamp(s.OverlayScale, 0.02, 1);
        (OverlayHAlign, OverlayVAlign) = s.OverlayAnchor switch
        {
            OverlayAnchor.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top),
            OverlayAnchor.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top),
            OverlayAnchor.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom),
            OverlayAnchor.BottomRight => (HorizontalAlignment.Right, VerticalAlignment.Bottom),
            _ => (HorizontalAlignment.Center, VerticalAlignment.Center),
        };

        if (s.OverlayImagePath != _overlayImagePath)
        {
            _overlayImagePath = s.OverlayImagePath;
            var old = _overlayImage;
            Bitmap? loaded = null;
            if (!string.IsNullOrWhiteSpace(s.OverlayImagePath) && File.Exists(s.OverlayImagePath))
            {
                try { loaded = new Bitmap(s.OverlayImagePath); }
                catch (Exception ex) { Log.Warning(ex, "Failed to load overlay logo {Path}", s.OverlayImagePath); }
            }
            OverlayImage = loaded;
            SafeBitmapDisposal.Retire(old);
        }

        AlertText = s.AlertText ?? string.Empty;
    }

    private static FontFamily ResolveFontFamily(string name)
    {
        try { return string.IsNullOrWhiteSpace(name) ? FontFamily.Default : new FontFamily(name); }
        catch { return FontFamily.Default; }
    }

    private static IBrush ParseBrush(string hex, IBrush fallback)
    {
        try { return new SolidColorBrush(Color.Parse(hex)); }
        catch { return fallback; }
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return Color.Parse(hex); }
        catch { return fallback; }
    }
}

/// <summary>Bindable geometry + alignment for one positioned text box on the projector canvas.</summary>
public sealed class RegionVm : ReactiveObject
{
    private double _x, _y, _width, _height;
    private bool _visible = true;
    private VerticalAlignment _vAlign = VerticalAlignment.Center;
    private TextAlignment _textAlign = TextAlignment.Center;
    private bool _autoFit;
    private double _minFontSize = 24;
    private double _maxFontSize = 140;
    private double _lineSpacing = 1.25;
    private IBrush _backgroundBrush = Brushes.Transparent;
    private CornerRadius _backgroundCorner;
    private Bitmap? _stillImage;
    private Bitmap? _liveFrame;
    private bool _useLiveBackground;
    private Stretch _backgroundImageStretch = Stretch.UniformToFill;
    private ITransform? _backgroundImageTransform;
    private Thickness _textPadding;

    public double X { get => _x; set => this.RaiseAndSetIfChanged(ref _x, value); }
    public double Y { get => _y; set => this.RaiseAndSetIfChanged(ref _y, value); }
    public double Width { get => _width; set => this.RaiseAndSetIfChanged(ref _width, value); }
    public double Height { get => _height; set => this.RaiseAndSetIfChanged(ref _height, value); }
    public bool Visible { get => _visible; set => this.RaiseAndSetIfChanged(ref _visible, value); }
    public VerticalAlignment VAlign { get => _vAlign; set => this.RaiseAndSetIfChanged(ref _vAlign, value); }
    public TextAlignment TextAlign { get => _textAlign; set => this.RaiseAndSetIfChanged(ref _textAlign, value); }
    public bool AutoFit { get => _autoFit; set => this.RaiseAndSetIfChanged(ref _autoFit, value); }
    public double MinFontSize { get => _minFontSize; set => this.RaiseAndSetIfChanged(ref _minFontSize, value); }
    public double MaxFontSize { get => _maxFontSize; set => this.RaiseAndSetIfChanged(ref _maxFontSize, value); }
    public double LineSpacing { get => _lineSpacing; set => this.RaiseAndSetIfChanged(ref _lineSpacing, value); }

    /// <summary>Fill painted behind the text (the caption "box").</summary>
    public IBrush BackgroundBrush { get => _backgroundBrush; set => this.RaiseAndSetIfChanged(ref _backgroundBrush, value); }
    public CornerRadius BackgroundCorner { get => _backgroundCorner; set => this.RaiseAndSetIfChanged(ref _backgroundCorner, value); }

    /// <summary>How the caption-box image is fitted (Fill / Uniform / UniformToFill).</summary>
    public Stretch BackgroundImageStretch { get => _backgroundImageStretch; set => this.RaiseAndSetIfChanged(ref _backgroundImageStretch, value); }

    /// <summary>Pan/zoom applied to the caption image so it can be slid around inside the (clipped) box.</summary>
    public ITransform? BackgroundImageTransform { get => _backgroundImageTransform; set => this.RaiseAndSetIfChanged(ref _backgroundImageTransform, value); }

    /// <summary>Inner padding between the box edges and the text.</summary>
    public Thickness TextPadding { get => _textPadding; set => this.RaiseAndSetIfChanged(ref _textPadding, value); }

    /// <summary>True when this box paints the operator's live background (image or video) rather than a still.</summary>
    public bool UseLiveBackground { get => _useLiveBackground; private set => this.RaiseAndSetIfChanged(ref _useLiveBackground, value); }

    /// <summary>The bitmap actually painted in the box: the live frame when following the live background,
    /// otherwise the still caption image.</summary>
    public Bitmap? DisplayImage => _useLiveBackground ? _liveFrame : _stillImage;
    public bool HasDisplayImage => DisplayImage is not null;

    /// <summary>Pushes a new live-background frame; only repaints when this box follows the live layer.</summary>
    public void SetLiveFrame(Bitmap? frame)
    {
        _liveFrame = frame;
        if (_useLiveBackground)
        {
            this.RaisePropertyChanged(nameof(DisplayImage));
            this.RaisePropertyChanged(nameof(HasDisplayImage));
        }
    }

    public void Apply(ThemeRegion r, double lineSpacing)
    {
        X = r.X;
        Y = r.Y;
        Width = r.Width;
        Height = r.Height;
        Visible = r.Visible;
        AutoFit = r.AutoFit;
        MinFontSize = r.MinFontSize;
        MaxFontSize = r.MaxFontSize;
        LineSpacing = lineSpacing;
        BackgroundCorner = new CornerRadius(r.BackgroundCornerRadius);
        try { BackgroundBrush = new SolidColorBrush(Color.Parse(r.BackgroundColor)); }
        catch { BackgroundBrush = Brushes.Transparent; }

        BackgroundImageStretch = r.BackgroundImageFit switch
        {
            ThemeImageFit.Fill => Stretch.Fill,
            ThemeImageFit.Uniform => Stretch.Uniform,
            _ => Stretch.UniformToFill,
        };
        BackgroundImageTransform = BuildImageTransform(r.BackgroundImageOffsetX, r.BackgroundImageOffsetY, r.BackgroundImageZoom);
        TextPadding = new Thickness(r.TextPaddingX, r.TextPaddingY);

        _stillImage = null;
        if (!string.IsNullOrWhiteSpace(r.BackgroundImagePath) && File.Exists(r.BackgroundImagePath))
        {
            try { _stillImage = new Bitmap(r.BackgroundImagePath); }
            catch (Exception ex) { Log.Warning(ex, "Failed to load caption image {Path}", r.BackgroundImagePath); }
        }

        UseLiveBackground = r.UseLiveBackground;
        this.RaisePropertyChanged(nameof(DisplayImage));
        this.RaisePropertyChanged(nameof(HasDisplayImage));

        VAlign = r.VAlign switch
        {
            ThemeVerticalAlign.Top => VerticalAlignment.Top,
            ThemeVerticalAlign.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
        TextAlign = r.HAlign switch
        {
            ThemeTextAlign.Left => TextAlignment.Left,
            ThemeTextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
    }

    /// <summary>Builds a centered zoom + pan transform for an image inside a box, or null when it's the identity.</summary>
    internal static ITransform? BuildImageTransform(double offsetX, double offsetY, double zoom)
    {
        if (zoom <= 0) zoom = 1;
        if (offsetX == 0 && offsetY == 0 && Math.Abs(zoom - 1) < 0.001) return null;
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(zoom, zoom));
        group.Children.Add(new TranslateTransform(offsetX, offsetY));
        return group;
    }
}

/// <summary>Bindable decorative shape positioned on the projector canvas.</summary>
public sealed class ShapeVm : ReactiveObject
{
    private readonly Bitmap? _stillImage;
    private Bitmap? _liveFrame;

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public IBrush Brush { get; }
    public CornerRadius CornerRadius { get; }
    public double Opacity { get; }
    public Stretch ImageStretch { get; }
    public ITransform? ImageTransform { get; }
    public bool UseLiveBackground { get; }

    /// <summary>The bitmap painted inside the shape: the live frame when following the live background,
    /// otherwise the still image.</summary>
    public Bitmap? DisplayImage => UseLiveBackground ? _liveFrame : _stillImage;
    public bool HasDisplayImage => DisplayImage is not null;

    public ShapeVm(ThemeShape s)
    {
        X = s.X;
        Y = s.Y;
        Width = s.Width;
        Height = s.Height;
        CornerRadius = new CornerRadius(s.CornerRadius);
        Opacity = Math.Clamp(s.Opacity, 0, 1);
        try { Brush = new SolidColorBrush(Color.Parse(s.Color)); }
        catch { Brush = new SolidColorBrush(Color.Parse("#80FFFFFF")); }

        ImageStretch = s.ImageFit switch
        {
            ThemeImageFit.Fill => Stretch.Fill,
            ThemeImageFit.Uniform => Stretch.Uniform,
            _ => Stretch.UniformToFill,
        };
        ImageTransform = RegionVm.BuildImageTransform(s.ImageOffsetX, s.ImageOffsetY, s.ImageZoom);
        UseLiveBackground = s.UseLiveBackground;

        if (!string.IsNullOrWhiteSpace(s.ImagePath) && File.Exists(s.ImagePath))
        {
            try { _stillImage = new Bitmap(s.ImagePath); }
            catch (Exception ex) { Log.Warning(ex, "Failed to load shape image {Path}", s.ImagePath); }
        }
    }

    /// <summary>Pushes a new live-background frame; only repaints when this shape follows the live layer.</summary>
    public void SetLiveFrame(Bitmap? frame)
    {
        _liveFrame = frame;
        if (UseLiveBackground)
        {
            this.RaisePropertyChanged(nameof(DisplayImage));
            this.RaisePropertyChanged(nameof(HasDisplayImage));
        }
    }
}
