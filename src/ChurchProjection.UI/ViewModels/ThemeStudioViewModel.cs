using System.Collections.ObjectModel;
using System.Reactive;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Theme;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Services;
using ChurchProjection.UI.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels;

/// <summary>
/// Backs the Theme Studio: lets the operator create/edit named themes with a live preview and
/// assign a theme to each content type (scripture / song / announcement).
/// </summary>
public sealed class ThemeStudioViewModel : ViewModelBase, IDisposable
{
    private readonly IThemeService _themes;

    private Theme _draft = new();
    private string? _editingOriginalName;
    private bool _loading;

    public ProjectorViewModel Preview { get; }

    public ObservableCollection<string> ThemeNames { get; } = [];
    public List<string> FontFamilies { get; }
    public IReadOnlyList<ThemeBackgroundKind> BackgroundKinds { get; } = Enum.GetValues<ThemeBackgroundKind>();
    public IReadOnlyList<ThemeTextAlign> Alignments { get; } = Enum.GetValues<ThemeTextAlign>();
    public IReadOnlyList<ThemeLayout> Layouts { get; } = Enum.GetValues<ThemeLayout>();
    public IReadOnlyList<ThemeImageFit> ImageFits { get; } = Enum.GetValues<ThemeImageFit>();

    public ReactiveCommand<Unit, Unit> NewThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> AddShapeCommand { get; }
    public ReactiveCommand<Unit, Unit> AddBarCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveShapeCommand { get; }

    /// <summary>Deletes the selected object: removes a shape, or hides a text region (Title/Body/Footer
    /// are fixed slots that can't be removed, so deleting them hides them from the layout).</summary>
    public ReactiveCommand<Unit, Unit> DeleteSelectedCommand { get; }

    // Z-order: reorder the selected shape within the shape stack (later = painted on top).
    public ReactiveCommand<Unit, Unit> BringToFrontCommand { get; }
    public ReactiveCommand<Unit, Unit> BringForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> SendBackwardCommand { get; }
    public ReactiveCommand<Unit, Unit> SendToBackCommand { get; }

    public ThemeStudioViewModel(IThemeService themes, ILiveBackgroundService? liveBackground = null)
    {
        _themes = themes;
        Preview = new ProjectorViewModel(new ProjectionService(), themes, null, liveBackground);

        FontFamilies = FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        RefreshThemeNames();
        _scriptureTheme = _themes.GetAssignment(SlideType.Scripture);
        _songTheme = _themes.GetAssignment(SlideType.Lyric);
        _announcementTheme = _themes.GetAssignment(SlideType.Announcement);

        var first = _themes.Themes.FirstOrDefault();
        if (first is not null) LoadDraft(first);
        else EnsureDraftRegions();

        NewThemeCommand = ReactiveCommand.CreateFromTask(NewThemeAsync);
        DuplicateThemeCommand = ReactiveCommand.CreateFromTask(DuplicateThemeAsync);
        DeleteThemeCommand = ReactiveCommand.CreateFromTask(DeleteThemeAsync);
        SaveThemeCommand = ReactiveCommand.CreateFromTask(SaveThemeAsync);
        AddShapeCommand = ReactiveCommand.Create(AddShape);
        AddBarCommand = ReactiveCommand.Create(AddBar);
        RemoveShapeCommand = ReactiveCommand.Create(RemoveShape);
        DeleteSelectedCommand = ReactiveCommand.Create(DeleteSelected);
        BringToFrontCommand = ReactiveCommand.Create(BringToFront);
        BringForwardCommand = ReactiveCommand.Create(BringForward);
        SendBackwardCommand = ReactiveCommand.Create(SendBackward);
        SendToBackCommand = ReactiveCommand.Create(SendToBack);
    }

    private string? _selectedThemeName;
    public string? SelectedThemeName
    {
        get => _selectedThemeName;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedThemeName, value);
            if (value is not null && _themes.GetByName(value) is { } t)
                LoadDraft(t);
        }
    }

    // --- Editor properties (backed by the working draft) ---

    public string Name { get => _draft.Name; set => SetDraft(v => _draft.Name = v, value); }
    public string FontFamilyName { get => _draft.FontFamily; set => SetDraft(v => _draft.FontFamily = v, value); }
    public double BodyFontSize { get => _draft.BodyFontSize; set => SetDraft(v => _draft.BodyFontSize = v, value); }
    public double TitleFontSize { get => _draft.TitleFontSize; set => SetDraft(v => _draft.TitleFontSize = v, value); }
    public double FooterFontSize { get => _draft.FooterFontSize; set => SetDraft(v => _draft.FooterFontSize = v, value); }
    public bool Bold { get => _draft.Bold; set => SetDraft(v => _draft.Bold = v, value); }
    public double LineHeightMultiplier { get => _draft.LineHeightMultiplier; set => SetDraft(v => _draft.LineHeightMultiplier = v, value); }
    public ThemeTextAlign TextAlign { get => _draft.TextAlign; set => SetDraft(v => _draft.TextAlign = v, value); }
    public string TextColor { get => _draft.TextColor; set => SetDraft(v => _draft.TextColor = v, value); }
    public string TitleColor { get => _draft.TitleColor; set => SetDraft(v => _draft.TitleColor = v, value); }
    public string FooterColor { get => _draft.FooterColor; set => SetDraft(v => _draft.FooterColor = v, value); }
    public double PaddingHorizontal { get => _draft.PaddingHorizontal; set => SetDraft(v => _draft.PaddingHorizontal = v, value); }
    public double PaddingVertical { get => _draft.PaddingVertical; set => SetDraft(v => _draft.PaddingVertical = v, value); }
    public ThemeLayout Layout { get => _draft.Layout; set => SetDraft(v => _draft.Layout = v, value); }
    public ThemeBackgroundKind BackgroundKind { get => _draft.BackgroundKind; set => SetDraft(v => _draft.BackgroundKind = v, value); }
    public string BackgroundColor { get => _draft.BackgroundColor; set => SetDraft(v => _draft.BackgroundColor = v, value); }
    public string? BackgroundImagePath { get => _draft.BackgroundImagePath; set => SetDraft(v => _draft.BackgroundImagePath = v, value); }
    public bool OutlineEnabled { get => _draft.OutlineEnabled; set => SetDraft(v => _draft.OutlineEnabled = v, value); }
    public string OutlineColor { get => _draft.OutlineColor; set => SetDraft(v => _draft.OutlineColor = v, value); }
    public double OutlineWidth { get => _draft.OutlineWidth; set => SetDraft(v => _draft.OutlineWidth = v, value); }
    public bool ShadowEnabled { get => _draft.ShadowEnabled; set => SetDraft(v => _draft.ShadowEnabled = v, value); }
    public string ShadowColor { get => _draft.ShadowColor; set => SetDraft(v => _draft.ShadowColor = v, value); }
    public double ShadowBlur { get => _draft.ShadowBlur; set => SetDraft(v => _draft.ShadowBlur = v, value); }
    public double ShadowOffsetX { get => _draft.ShadowOffsetX; set => SetDraft(v => _draft.ShadowOffsetX = v, value); }
    public double ShadowOffsetY { get => _draft.ShadowOffsetY; set => SetDraft(v => _draft.ShadowOffsetY = v, value); }
    public double ShadowOpacity { get => _draft.ShadowOpacity; set => SetDraft(v => _draft.ShadowOpacity = v, value); }
    public ThemeImageFit ImageFit { get => _draft.ImageFit; set => SetDraft(v => _draft.ImageFit = v, value); }
    public string LowerThirdBarColor { get => _draft.LowerThirdBarColor; set => SetDraft(v => _draft.LowerThirdBarColor = v, value); }

    // --- Layout editor (positionable regions) ---

    /// <summary>Editor canvas scale: the 1920x1080 design space sized to fill the available editor area.</summary>
    private double _editorScale = 0.42;
    public double EditorScale
    {
        get => _editorScale;
        private set => this.RaiseAndSetIfChanged(ref _editorScale, value);
    }
    public double EditorCanvasWidth => Theme.CanvasWidth * _editorScale;
    public double EditorCanvasHeight => Theme.CanvasHeight * _editorScale;

    /// <summary>Fits the 16:9 design canvas into the available editor viewport (called on resize).</summary>
    public void SetViewport(double availableWidth, double availableHeight)
    {
        if (availableWidth < 80 || availableHeight < 80) return;
        var scale = Math.Min(availableWidth / Theme.CanvasWidth, availableHeight / Theme.CanvasHeight);
        scale = Math.Max(0.12, scale);
        if (Math.Abs(scale - _editorScale) < 0.0005) return;
        EditorScale = scale;
        this.RaisePropertyChanged(nameof(EditorCanvasWidth));
        this.RaisePropertyChanged(nameof(EditorCanvasHeight));
        RaiseRegionGeometry();
        RaiseSelectedRegionProps();
    }

    /// <summary>Raised after a successful save so the host window can close itself.</summary>
    public event Action? CloseRequested;

    public IReadOnlyList<RegionKind> RegionKinds { get; } = Enum.GetValues<RegionKind>();
    public IReadOnlyList<ThemeVerticalAlign> VerticalAligns { get; } = Enum.GetValues<ThemeVerticalAlign>();

    private RegionKind _selectedRegion = RegionKind.Body;
    public RegionKind SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRegion, value);
            // Picking a text region clears any shape selection.
            if (_selectedShapeIndex != -1) { _selectedShapeIndex = -1; this.RaisePropertyChanged(nameof(SelectedShapeIndex)); }
            RaiseRegionSelection();
            RaiseSelectedRegionProps();
        }
    }

    /// <summary>Unified list of editable objects (the three text regions plus each shape).</summary>
    public ObservableCollection<LayoutObjectItem> LayoutObjects { get; } = [];

    private bool _syncingSelection;
    private LayoutObjectItem? _selectedObject;
    public LayoutObjectItem? SelectedObject
    {
        get => _selectedObject;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedObject, value);
            if (_syncingSelection || value is null) return;
            _syncingSelection = true;
            if (value.IsShape) SelectedShapeIndex = value.ShapeIndex;
            else SelectedRegion = value.Region;
            _syncingSelection = false;
        }
    }

    private void RebuildLayoutObjects()
    {
        LayoutObjects.Clear();
        LayoutObjects.Add(new LayoutObjectItem { Name = "Title", Region = RegionKind.Title, Hidden = !(_draft.TitleRegion?.Visible ?? true) });
        LayoutObjects.Add(new LayoutObjectItem { Name = "Body", Region = RegionKind.Body, Hidden = !(_draft.BodyRegion?.Visible ?? true) });
        LayoutObjects.Add(new LayoutObjectItem { Name = "Footer", Region = RegionKind.Footer, Hidden = !(_draft.FooterRegion?.Visible ?? true) });
        for (var i = 0; i < _draft.Shapes.Count; i++)
            LayoutObjects.Add(new LayoutObjectItem { Name = $"Shape {i + 1}", IsShape = true, ShapeIndex = i });
        SyncSelectedObject();
    }

    private void SyncSelectedObject()
    {
        _syncingSelection = true;
        _selectedObject = ShapeMode
            ? LayoutObjects.FirstOrDefault(o => o.IsShape && o.ShapeIndex == _selectedShapeIndex)
            : LayoutObjects.FirstOrDefault(o => !o.IsShape && o.Region == _selectedRegion);
        this.RaisePropertyChanged(nameof(SelectedObject));
        _syncingSelection = false;
    }

    // Per-region font size + text colour routed to the right theme property for the selected region.
    public double SelFontSize
    {
        get => _selectedRegion switch { RegionKind.Title => _draft.TitleFontSize, RegionKind.Footer => _draft.FooterFontSize, _ => _draft.BodyFontSize };
        set
        {
            switch (_selectedRegion)
            {
                case RegionKind.Title: _draft.TitleFontSize = value; this.RaisePropertyChanged(nameof(TitleFontSize)); break;
                case RegionKind.Footer: _draft.FooterFontSize = value; this.RaisePropertyChanged(nameof(FooterFontSize)); break;
                default: _draft.BodyFontSize = value; this.RaisePropertyChanged(nameof(BodyFontSize)); break;
            }
            this.RaisePropertyChanged(nameof(SelFontSize));
            if (!_loading) RefreshPreview();
        }
    }

    public string SelTextColor
    {
        get => _selectedRegion switch { RegionKind.Title => _draft.TitleColor, RegionKind.Footer => _draft.FooterColor, _ => _draft.TextColor };
        set
        {
            switch (_selectedRegion)
            {
                case RegionKind.Title: _draft.TitleColor = value; this.RaisePropertyChanged(nameof(TitleColor)); break;
                case RegionKind.Footer: _draft.FooterColor = value; this.RaisePropertyChanged(nameof(FooterColor)); break;
                default: _draft.TextColor = value; this.RaisePropertyChanged(nameof(TextColor)); break;
            }
            this.RaisePropertyChanged(nameof(SelTextColor));
            if (!_loading) RefreshPreview();
        }
    }

    /// <summary>Decorative shapes on the current draft (synced with <c>_draft.Shapes</c>).</summary>
    public ObservableCollection<ThemeShape> Shapes { get; } = [];

    private int _selectedShapeIndex = -1;
    public int SelectedShapeIndex
    {
        get => _selectedShapeIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedShapeIndex, value);
            RaiseRegionSelection();
            RaiseSelectedRegionProps();
        }
    }

    private void AddBar()
    {
        var s = new ThemeShape { X = 660, Y = 360, Width = 600, Height = 8, Color = "#FFFFFFFF", CornerRadius = 4 };
        _draft.Shapes.Add(s);
        Shapes.Add(s);
        RebuildLayoutObjects();
        SelectedShapeIndex = _draft.Shapes.Count - 1;
        if (!_loading) RefreshPreview();
    }

    private void AddShape()
    {
        var s = new ThemeShape { X = 460, Y = 470, Width = 1000, Height = 140, Color = "#A6111418", CornerRadius = 16 };
        _draft.Shapes.Add(s);
        Shapes.Add(s);
        RebuildLayoutObjects();
        SelectedShapeIndex = _draft.Shapes.Count - 1;
        if (!_loading) RefreshPreview();
    }

    private void RemoveShape()
    {
        var i = _selectedShapeIndex;
        if (i < 0 || i >= _draft.Shapes.Count) return;
        _draft.Shapes.RemoveAt(i);
        Shapes.RemoveAt(i);
        SelectedShapeIndex = -1;
        SelectedRegion = RegionKind.Body;
        RebuildLayoutObjects();
        if (!_loading) RefreshPreview();
    }

    /// <summary>Deletes whatever object is selected. Shapes are removed outright; the three fixed text
    /// regions can't be removed, so we hide them (the user can re-show them from the Layout tab).</summary>
    private void DeleteSelected()
    {
        if (ShapeMode)
        {
            RemoveShape();
            return;
        }

        CurRegion.Visible = false;
        AfterRegionEdit();
        RebuildLayoutObjects();
    }

    // --- Shape z-order ---
    private void BringToFront() => MoveShape(_draft.Shapes.Count - 1);
    private void SendToBack() => MoveShape(0);
    private void BringForward() => MoveShape(_selectedShapeIndex + 1);
    private void SendBackward() => MoveShape(_selectedShapeIndex - 1);

    /// <summary>Moves the selected shape to a new index in the stack (shapes are painted in list order,
    /// so a higher index renders in front). Keeps the moved shape selected.</summary>
    private void MoveShape(int newIndex)
    {
        var i = _selectedShapeIndex;
        if (i < 0 || i >= _draft.Shapes.Count) return;
        newIndex = Math.Max(0, Math.Min(_draft.Shapes.Count - 1, newIndex));
        if (newIndex == i) return;

        var shape = _draft.Shapes[i];
        _draft.Shapes.RemoveAt(i);
        _draft.Shapes.Insert(newIndex, shape);
        Shapes.Move(i, newIndex);

        SelectedShapeIndex = newIndex;
        RebuildLayoutObjects();
        if (!_loading) RefreshPreview();
    }

    private ThemeRegion CurRegion => _selectedRegion switch
    {
        RegionKind.Title => _draft.TitleRegion!,
        RegionKind.Footer => _draft.FooterRegion!,
        _ => _draft.BodyRegion!,
    };

    private ThemeShape? CurShape =>
        _selectedShapeIndex >= 0 && _selectedShapeIndex < _draft.Shapes.Count ? _draft.Shapes[_selectedShapeIndex] : null;

    /// <summary>True when a decorative shape (rather than a text region) is the current edit target.</summary>
    private bool ShapeMode => CurShape is not null;

    // Shared geometry accessors that route to whichever object (region or shape) is selected.
    private double GeoX { get => ShapeMode ? CurShape!.X : CurRegion.X; set { if (ShapeMode) CurShape!.X = value; else CurRegion.X = value; } }
    private double GeoY { get => ShapeMode ? CurShape!.Y : CurRegion.Y; set { if (ShapeMode) CurShape!.Y = value; else CurRegion.Y = value; } }
    private double GeoW { get => ShapeMode ? CurShape!.Width : CurRegion.Width; set { if (ShapeMode) CurShape!.Width = value; else CurRegion.Width = value; } }
    private double GeoH { get => ShapeMode ? CurShape!.Height : CurRegion.Height; set { if (ShapeMode) CurShape!.Height = value; else CurRegion.Height = value; } }

    // Selected target — editable in design (1920x1080) space.
    public double SelX { get => GeoX; set { GeoX = Clamp(value, 0, Theme.CanvasWidth - GeoW); AfterRegionEdit(); } }
    public double SelY { get => GeoY; set { GeoY = Clamp(value, 0, Theme.CanvasHeight - GeoH); AfterRegionEdit(); } }
    public double SelWidth { get => GeoW; set { GeoW = Clamp(value, 20, Theme.CanvasWidth - GeoX); AfterRegionEdit(); } }
    public double SelHeight { get => GeoH; set { GeoH = Clamp(value, 20, Theme.CanvasHeight - GeoY); AfterRegionEdit(); } }
    public bool SelVisible
    {
        get => ShapeMode || CurRegion.Visible;
        set
        {
            if (ShapeMode) { AfterRegionEdit(); return; }
            CurRegion.Visible = value;
            AfterRegionEdit();
            RebuildLayoutObjects();
        }
    }
    public ThemeTextAlign SelHAlign { get => CurRegion.HAlign; set { CurRegion.HAlign = value; AfterRegionEdit(); } }
    public ThemeVerticalAlign SelVAlign { get => CurRegion.VAlign; set { CurRegion.VAlign = value; AfterRegionEdit(); } }
    public bool SelAutoFit { get => CurRegion.AutoFit; set { CurRegion.AutoFit = value; AfterRegionEdit(); } }
    public double SelMinFontSize { get => CurRegion.MinFontSize; set { CurRegion.MinFontSize = value; AfterRegionEdit(); } }
    public double SelMaxFontSize { get => CurRegion.MaxFontSize; set { CurRegion.MaxFontSize = value; AfterRegionEdit(); } }

    // Region caption-box (the coloured box painted behind the text; transparent by default).
    public string SelRegionBgColor { get => CurRegion.BackgroundColor; set { CurRegion.BackgroundColor = value; AfterRegionEdit(); } }
    public double SelRegionCorner { get => CurRegion.BackgroundCornerRadius; set { CurRegion.BackgroundCornerRadius = value; AfterRegionEdit(); } }
    public string? SelRegionBgImagePath { get => CurRegion.BackgroundImagePath; set { CurRegion.BackgroundImagePath = value; AfterRegionEdit(); } }
    public ThemeImageFit SelRegionBgImageFit { get => CurRegion.BackgroundImageFit; set { CurRegion.BackgroundImageFit = value; AfterRegionEdit(); } }
    public double SelRegionImageOffsetX { get => CurRegion.BackgroundImageOffsetX; set { CurRegion.BackgroundImageOffsetX = value; AfterRegionEdit(); } }
    public double SelRegionImageOffsetY { get => CurRegion.BackgroundImageOffsetY; set { CurRegion.BackgroundImageOffsetY = value; AfterRegionEdit(); } }
    public double SelRegionImageZoom { get => CurRegion.BackgroundImageZoom; set { CurRegion.BackgroundImageZoom = value; AfterRegionEdit(); } }
    public bool SelRegionUseLiveBackground { get => CurRegion.UseLiveBackground; set { CurRegion.UseLiveBackground = value; AfterRegionEdit(); } }

    // Inner text padding for the selected region's box.
    public double SelTextPaddingX { get => CurRegion.TextPaddingX; set { CurRegion.TextPaddingX = value; AfterRegionEdit(); } }
    public double SelTextPaddingY { get => CurRegion.TextPaddingY; set { CurRegion.TextPaddingY = value; AfterRegionEdit(); } }

    // Shape-only properties.
    public bool IsShapeSelected => ShapeMode;
    public bool IsRegionSelected => !ShapeMode;
    public string SelObjectName => ShapeMode ? $"Shape {_selectedShapeIndex + 1}" : _selectedRegion.ToString();
    public string SelShapeColor { get => CurShape?.Color ?? "#80FFFFFF"; set { if (CurShape is not null) { CurShape.Color = value; AfterRegionEdit(); } } }
    public double SelShapeCorner { get => CurShape?.CornerRadius ?? 0; set { if (CurShape is not null) { CurShape.CornerRadius = value; AfterRegionEdit(); } } }
    public double SelShapeOpacity { get => CurShape?.Opacity ?? 1.0; set { if (CurShape is not null) { CurShape.Opacity = value; AfterRegionEdit(); } } }
    public string? SelShapeImagePath { get => CurShape?.ImagePath; set { if (CurShape is not null) { CurShape.ImagePath = value; AfterRegionEdit(); } } }
    public ThemeImageFit SelShapeImageFit { get => CurShape?.ImageFit ?? ThemeImageFit.UniformToFill; set { if (CurShape is not null) { CurShape.ImageFit = value; AfterRegionEdit(); } } }
    public double SelShapeImageOffsetX { get => CurShape?.ImageOffsetX ?? 0; set { if (CurShape is not null) { CurShape.ImageOffsetX = value; AfterRegionEdit(); } } }
    public double SelShapeImageOffsetY { get => CurShape?.ImageOffsetY ?? 0; set { if (CurShape is not null) { CurShape.ImageOffsetY = value; AfterRegionEdit(); } } }
    public double SelShapeImageZoom { get => CurShape?.ImageZoom ?? 1.0; set { if (CurShape is not null) { CurShape.ImageZoom = value; AfterRegionEdit(); } } }
    public bool SelShapeUseLiveBackground { get => CurShape?.UseLiveBackground ?? false; set { if (CurShape is not null) { CurShape.UseLiveBackground = value; AfterRegionEdit(); } } }

    // Editor-space rectangles for drawing the three boxes.
    public double TitleBoxX => _draft.TitleRegion!.X * EditorScale;
    public double TitleBoxY => _draft.TitleRegion!.Y * EditorScale;
    public double TitleBoxW => _draft.TitleRegion!.Width * EditorScale;
    public double TitleBoxH => _draft.TitleRegion!.Height * EditorScale;
    public bool TitleBoxVisible => _draft.TitleRegion!.Visible;
    public bool IsTitleSelected => _selectedRegion == RegionKind.Title;

    public double BodyBoxX => _draft.BodyRegion!.X * EditorScale;
    public double BodyBoxY => _draft.BodyRegion!.Y * EditorScale;
    public double BodyBoxW => _draft.BodyRegion!.Width * EditorScale;
    public double BodyBoxH => _draft.BodyRegion!.Height * EditorScale;
    public bool BodyBoxVisible => _draft.BodyRegion!.Visible;
    public bool IsBodySelected => _selectedRegion == RegionKind.Body;

    public double FooterBoxX => _draft.FooterRegion!.X * EditorScale;
    public double FooterBoxY => _draft.FooterRegion!.Y * EditorScale;
    public double FooterBoxW => _draft.FooterRegion!.Width * EditorScale;
    public double FooterBoxH => _draft.FooterRegion!.Height * EditorScale;
    public bool FooterBoxVisible => _draft.FooterRegion!.Visible;
    public bool IsFooterSelected => _selectedRegion == RegionKind.Footer;

    // Selection overlay (the box that has the drag/resize handles).
    public double SelBoxX => GeoX * EditorScale;
    public double SelBoxY => GeoY * EditorScale;
    public double SelBoxW => GeoW * EditorScale;
    public double SelBoxH => GeoH * EditorScale;

    /// <summary>Drag the selected target by an editor-space delta (from the move thumb).</summary>
    public void MoveSelected(double dxEditor, double dyEditor)
    {
        GeoX = Clamp(GeoX + dxEditor / EditorScale, 0, Theme.CanvasWidth - GeoW);
        GeoY = Clamp(GeoY + dyEditor / EditorScale, 0, Theme.CanvasHeight - GeoH);
        AfterRegionEdit();
    }

    /// <summary>Resize the selected target from a handle ("tl","t","tr","l","r","bl","b","br").</summary>
    public void ResizeSelected(string handle, double dxEditor, double dyEditor)
    {
        var dx = dxEditor / EditorScale;
        var dy = dyEditor / EditorScale;
        double x = GeoX, y = GeoY, w = GeoW, h = GeoH;
        double ox = x, oy = y, ow = w, oh = h;

        if (handle.Contains('l')) { x += dx; w -= dx; }
        if (handle.Contains('r')) { w += dx; }
        if (handle.Contains('t')) { y += dy; h -= dy; }
        if (handle.Contains('b')) { h += dy; }

        if (w < 20) { if (handle.Contains('l')) x = ox + ow - 20; w = 20; }
        if (h < 20) { if (handle.Contains('t')) y = oy + oh - 20; h = 20; }

        x = Math.Max(0, x);
        y = Math.Max(0, y);
        if (x + w > Theme.CanvasWidth) w = Theme.CanvasWidth - x;
        if (y + h > Theme.CanvasHeight) h = Theme.CanvasHeight - y;

        GeoX = x; GeoY = y; GeoW = w; GeoH = h;
        AfterRegionEdit();
    }

    private void AfterRegionEdit()
    {
        RaiseSelectedRegionProps();
        RaiseRegionGeometry();
        if (!_loading) RefreshPreview();
    }

    private void RaiseSelectedRegionProps()
    {
        foreach (var p in new[]
        {
            nameof(SelX), nameof(SelY), nameof(SelWidth), nameof(SelHeight), nameof(SelVisible),
            nameof(SelHAlign), nameof(SelVAlign), nameof(SelAutoFit), nameof(SelMinFontSize), nameof(SelMaxFontSize),
            nameof(SelBoxX), nameof(SelBoxY), nameof(SelBoxW), nameof(SelBoxH),
            nameof(IsShapeSelected), nameof(IsRegionSelected),
            nameof(SelShapeColor), nameof(SelShapeCorner), nameof(SelShapeOpacity),
            nameof(SelShapeImagePath), nameof(SelShapeImageFit),
            nameof(SelShapeImageOffsetX), nameof(SelShapeImageOffsetY), nameof(SelShapeImageZoom), nameof(SelShapeUseLiveBackground),
            nameof(SelRegionBgColor), nameof(SelRegionCorner), nameof(SelRegionBgImagePath), nameof(SelRegionBgImageFit),
            nameof(SelRegionImageOffsetX), nameof(SelRegionImageOffsetY), nameof(SelRegionImageZoom), nameof(SelRegionUseLiveBackground),
            nameof(SelTextPaddingX), nameof(SelTextPaddingY),
            nameof(SelFontSize), nameof(SelTextColor), nameof(SelObjectName),
        })
            this.RaisePropertyChanged(p);
    }

    private void RaiseRegionSelection()
    {
        this.RaisePropertyChanged(nameof(IsTitleSelected));
        this.RaisePropertyChanged(nameof(IsBodySelected));
        this.RaisePropertyChanged(nameof(IsFooterSelected));
        this.RaisePropertyChanged(nameof(SelBoxX));
        this.RaisePropertyChanged(nameof(SelBoxY));
        this.RaisePropertyChanged(nameof(SelBoxW));
        this.RaisePropertyChanged(nameof(SelBoxH));
        SyncSelectedObject();
    }

    private void RaiseRegionGeometry()
    {
        foreach (var p in new[]
        {
            nameof(TitleBoxX), nameof(TitleBoxY), nameof(TitleBoxW), nameof(TitleBoxH), nameof(TitleBoxVisible),
            nameof(BodyBoxX), nameof(BodyBoxY), nameof(BodyBoxW), nameof(BodyBoxH), nameof(BodyBoxVisible),
            nameof(FooterBoxX), nameof(FooterBoxY), nameof(FooterBoxW), nameof(FooterBoxH), nameof(FooterBoxVisible),
        })
            this.RaisePropertyChanged(p);
    }

    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, Math.Max(lo, v)));

    /// <summary>Materializes the draft's layout regions so the editor always has concrete boxes
    /// to manipulate. Older/auto themes derive them from padding; once edited + saved they're explicit.</summary>
    private void EnsureDraftRegions()
    {
        var (t, b, f) = _draft.ResolveRegions();
        _draft.TitleRegion = t.Clone();
        _draft.BodyRegion = b.Clone();
        _draft.FooterRegion = f.Clone();
    }

    // --- Per-content-type assignments ---

    private string _scriptureTheme = "";
    public string ScriptureTheme
    {
        get => _scriptureTheme;
        set { this.RaiseAndSetIfChanged(ref _scriptureTheme, value); _ = AssignAsync(SlideType.Scripture, value); }
    }

    private string _songTheme = "";
    public string SongTheme
    {
        get => _songTheme;
        set { this.RaiseAndSetIfChanged(ref _songTheme, value); _ = AssignAsync(SlideType.Lyric, value); }
    }

    private string _announcementTheme = "";
    public string AnnouncementTheme
    {
        get => _announcementTheme;
        set { this.RaiseAndSetIfChanged(ref _announcementTheme, value); _ = AssignAsync(SlideType.Announcement, value); }
    }

    private async Task AssignAsync(SlideType type, string themeName)
    {
        if (_loading || string.IsNullOrEmpty(themeName)) return;
        await _themes.SetAssignmentAsync(type, themeName);
    }

    private void SetDraft<T>(Action<T> apply, T value, [CallerMemberName] string? name = null)
    {
        apply(value);
        this.RaisePropertyChanged(name!);
        if (!_loading) RefreshPreview();
    }

    private void LoadDraft(Theme theme)
    {
        _loading = true;
        _draft = theme.Clone();
        EnsureDraftRegions();

        Shapes.Clear();
        foreach (var s in _draft.Shapes) Shapes.Add(s);
        _selectedShapeIndex = -1;
        this.RaisePropertyChanged(nameof(SelectedShapeIndex));
        RebuildLayoutObjects();

        _editingOriginalName = theme.Name;
        _selectedThemeName = theme.Name;
        this.RaisePropertyChanged(nameof(SelectedThemeName));
        RaiseAllEditorProps();
        RaiseSelectedRegionProps();
        RaiseRegionSelection();
        RaiseRegionGeometry();
        _loading = false;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        Preview.SetSampleContent(
            "John 3:16",
            "For God so loved the world that he gave his one and only Son, that whoever believes in him shall not perish but have eternal life.",
            $"John 3:16  -  {(_selectedThemeName ?? Name)}");
        Preview.PreviewTheme(_draft);
    }

    private void RefreshThemeNames()
    {
        ThemeNames.Clear();
        foreach (var t in _themes.Themes)
            ThemeNames.Add(t.Name);
    }

    private async Task NewThemeAsync()
    {
        var name = UniqueName("New Theme");
        var theme = new Theme { Name = name };
        await _themes.AddOrUpdateAsync(theme);
        RefreshThemeNames();
        SelectedThemeName = name;
    }

    private async Task DuplicateThemeAsync()
    {
        var copy = _draft.Clone();
        copy.Name = UniqueName($"{_draft.Name} copy");
        await _themes.AddOrUpdateAsync(copy);
        RefreshThemeNames();
        SelectedThemeName = copy.Name;
    }

    private async Task DeleteThemeAsync()
    {
        if (_editingOriginalName is null) return;
        await _themes.DeleteAsync(_editingOriginalName);
        RefreshThemeNames();
        var first = _themes.Themes.FirstOrDefault();
        if (first is not null) LoadDraft(first);
    }

    private async Task SaveThemeAsync()
    {
        if (string.IsNullOrWhiteSpace(_draft.Name))
            _draft.Name = UniqueName("Theme");

        await _themes.AddOrUpdateAsync(_draft.Clone(), _editingOriginalName);
        _editingOriginalName = _draft.Name;
        RefreshThemeNames();
        _selectedThemeName = _draft.Name;
        this.RaisePropertyChanged(nameof(SelectedThemeName));
        RefreshPreview();
        CloseRequested?.Invoke();
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var i = 2;
        while (_themes.GetByName(name) is not null)
            name = $"{baseName} {i++}";
        return name;
    }

    private void RaiseAllEditorProps()
    {
        foreach (var p in new[]
        {
            nameof(Name), nameof(FontFamilyName), nameof(BodyFontSize), nameof(TitleFontSize), nameof(FooterFontSize),
            nameof(Bold), nameof(LineHeightMultiplier), nameof(TextAlign), nameof(TextColor), nameof(TitleColor),
            nameof(FooterColor), nameof(PaddingHorizontal), nameof(PaddingVertical), nameof(Layout), nameof(BackgroundKind),
            nameof(BackgroundColor), nameof(BackgroundImagePath), nameof(OutlineEnabled), nameof(OutlineColor),
            nameof(OutlineWidth), nameof(ShadowEnabled), nameof(ShadowColor), nameof(ShadowBlur),
            nameof(ShadowOffsetX), nameof(ShadowOffsetY), nameof(ShadowOpacity), nameof(ImageFit), nameof(LowerThirdBarColor),
        })
        {
            this.RaisePropertyChanged(p);
        }
    }

    public void Dispose() => Preview.Dispose();
}

/// <summary>Which positionable text box the Layout editor is currently editing.</summary>
public enum RegionKind { Title, Body, Footer }

/// <summary>An entry in the studio's unified object list (a text region or a shape).</summary>
public sealed class LayoutObjectItem
{
    public string Name { get; init; } = "";
    public bool IsShape { get; init; }
    public int ShapeIndex { get; init; } = -1;
    public RegionKind Region { get; init; }

    /// <summary>True when a text region has been hidden (deleted) from the layout.</summary>
    public bool Hidden { get; init; }

    /// <summary>List label, annotated when the object is hidden.</summary>
    public string Display => Hidden ? $"{Name} (hidden)" : Name;
}
