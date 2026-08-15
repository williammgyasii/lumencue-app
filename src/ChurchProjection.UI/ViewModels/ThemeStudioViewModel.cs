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
/// Backs the Theme Studio: lets the operator create/edit named themes with a live preview.
/// One theme applies to all content types; layer bindings route title/body/footer per content type.
/// </summary>
public sealed class ThemeStudioViewModel : ViewModelBase, IDisposable
{
    private readonly IThemeService _themes;
    private readonly IThemeAssetStore? _assetStore;

    private Theme _draft = new();
    private string? _editingOriginalName;
    private bool _loading;

    public ProjectorViewModel Preview { get; }

    public ObservableCollection<string> ThemeNames { get; } = [];

    /// <summary>ThemeService never allows zero themes; hide delete when this is the last one.</summary>
    public bool CanDeleteTheme => ThemeNames.Count > 1;
    public List<string> FontFamilies { get; }
    public IReadOnlyList<ThemeBackgroundKind> BackgroundKinds { get; } = ThemeBackgroundStudio.EditorTypes;
    public IReadOnlyList<ThemeTextAlign> Alignments { get; } = Enum.GetValues<ThemeTextAlign>();
    public IReadOnlyList<ThemeLayout> Layouts { get; } = Enum.GetValues<ThemeLayout>();
    public IReadOnlyList<ThemeImageFit> ImageFits { get; } = Enum.GetValues<ThemeImageFit>();

    public IReadOnlyList<ThemeContentField> ContentFields { get; } = Enum.GetValues<ThemeContentField>();
    public IReadOnlyList<PreviewContentMode> PreviewModes { get; } = Enum.GetValues<PreviewContentMode>();

    public ReactiveCommand<Unit, Unit> NewThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> AddShapeCommand { get; }
    public ReactiveCommand<Unit, Unit> AddBarCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveShapeCommand { get; }

    /// <summary>Deletes the selected object: removes a shape, or removes a text layer from the layout.</summary>
    public ReactiveCommand<Unit, Unit> DeleteSelectedCommand { get; }

    public ReactiveCommand<RegionKind, Unit> AddTextLayerCommand { get; }

    // Z-order: reorder the selected shape within the shape stack (later = painted on top).
    public ReactiveCommand<Unit, Unit> BringToFrontCommand { get; }
    public ReactiveCommand<Unit, Unit> BringForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> SendBackwardCommand { get; }
    public ReactiveCommand<Unit, Unit> SendToBackCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowElementInspectorCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowThemeInspectorTabCommand { get; }

    public ReactiveCommand<RegionInspectorTab, Unit> SelectRegionTabCommand { get; }
    public ReactiveCommand<ShapeInspectorTab, Unit> SelectShapeTabCommand { get; }
    public ReactiveCommand<ThemeInspectorTab, Unit> SelectThemeTabCommand { get; }

    private bool _isSaving;
    private bool _showSaveSuccess;
    private CancellationTokenSource? _saveStatusCts;

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSaving, value);
            this.RaisePropertyChanged(nameof(ShowSaveProgress));
        }
    }

    public bool ShowSaveProgress => IsSaving;

    public bool ShowSaveSuccess
    {
        get => _showSaveSuccess;
        private set => this.RaiseAndSetIfChanged(ref _showSaveSuccess, value);
    }

    public ThemeStudioViewModel(IThemeService themes, ILiveBackgroundService? liveBackground = null, IThemeAssetStore? assetStore = null)
    {
        _themes = themes;
        _assetStore = assetStore;
        Preview = new ProjectorViewModel(new ProjectionService(), themes, null, liveBackground);

        FontFamilies = FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        RefreshThemeNames();

        var first = _themes.Themes.FirstOrDefault();
        if (first is not null) LoadDraft(first);
        else EnsureDraftRegions();

        NewThemeCommand = ReactiveCommand.CreateFromTask(NewThemeAsync);
        DuplicateThemeCommand = ReactiveCommand.CreateFromTask(DuplicateThemeAsync);
        DeleteThemeCommand = ReactiveCommand.CreateFromTask(DeleteThemeAsync);
        SaveThemeCommand = ReactiveCommand.CreateFromTask(
            SaveThemeAsync,
            this.WhenAnyValue(x => x.IsSaving, isSaving => !isSaving));
        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke());
        AddShapeCommand = ReactiveCommand.Create(AddShape);
        AddBarCommand = ReactiveCommand.Create(AddBar);
        RemoveShapeCommand = ReactiveCommand.Create(RemoveShape);
        DeleteSelectedCommand = ReactiveCommand.Create(DeleteSelected);
        AddTextLayerCommand = ReactiveCommand.Create<RegionKind>(AddTextLayer);
        BringToFrontCommand = ReactiveCommand.Create(BringToFront);
        BringForwardCommand = ReactiveCommand.Create(BringForward);
        SendBackwardCommand = ReactiveCommand.Create(SendBackward);
        SendToBackCommand = ReactiveCommand.Create(SendToBack);
        ShowElementInspectorCommand = ReactiveCommand.Create(ShowElementInspector);
        ShowThemeInspectorTabCommand = ReactiveCommand.Create(ShowThemeInspectorTab);
        SelectRegionTabCommand = ReactiveCommand.Create<RegionInspectorTab>(SetRegionInspectorTab);
        SelectShapeTabCommand = ReactiveCommand.Create<ShapeInspectorTab>(SetShapeInspectorTab);
        SelectThemeTabCommand = ReactiveCommand.Create<ThemeInspectorTab>(SetThemeInspectorTab);
    }

    private string? _selectedThemeName;
    private bool _syncingThemeList;

    public string? SelectedThemeName
    {
        get => _selectedThemeName;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedThemeName, value);
            if (_syncingThemeList || _loading) return;
            if (value is not null && _themes.GetByName(value) is { } t)
                LoadDraft(t);
        }
    }

    private PreviewContentMode _previewMode = PreviewContentMode.Scripture;
    public PreviewContentMode PreviewMode
    {
        get => _previewMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _previewMode, value);
            if (!_loading) RefreshPreview();
        }
    }

    public bool CanAddTitleLayer => _draft.TitleRegion is null;
    public bool CanAddBodyLayer => _draft.BodyRegion is null;
    public bool CanAddFooterLayer => _draft.FooterRegion is null;

    // --- Editor properties (backed by the working draft) ---

    public string Name { get => _draft.Name; set => SetDraft(v => _draft.Name = v, value); }
    public string FontFamilyName { get => _draft.FontFamily; set => SetDraft(v => _draft.FontFamily = v, value); }

    /// <summary>Font combo for the selected text region. Body writes <see cref="Theme.FontFamily"/>;
    /// title/footer write their own faces so they can differ from the body.</summary>
    public string SelFontFamily
    {
        get => _selectedRegion switch
        {
            RegionKind.Title => _draft.ResolveTitleFont(),
            RegionKind.Footer => _draft.ResolveFooterFont(),
            _ => _draft.ResolveBodyFont(),
        };
        set
        {
            switch (_selectedRegion)
            {
                case RegionKind.Title:
                    _draft.TitleFontFamily = value;
                    this.RaisePropertyChanged(nameof(TitleFontFamily));
                    break;
                case RegionKind.Footer:
                    _draft.FooterFontFamily = value;
                    this.RaisePropertyChanged(nameof(FooterFontFamily));
                    break;
                default:
                    _draft.FontFamily = value;
                    this.RaisePropertyChanged(nameof(FontFamilyName));
                    break;
            }
            this.RaisePropertyChanged(nameof(SelFontFamily));
            if (!_loading) RefreshPreview();
        }
    }

    public string TitleFontFamily { get => _draft.TitleFontFamily; set => SetDraft(v => _draft.TitleFontFamily = v, value); }
    public string FooterFontFamily { get => _draft.FooterFontFamily; set => SetDraft(v => _draft.FooterFontFamily = v, value); }
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
    public ThemeBackgroundKind BackgroundKind
    {
        get => ThemeBackgroundStudio.ForEditor(_draft.BackgroundKind);
        set
        {
            SetDraft(v => _draft.BackgroundKind = v, value);
            this.RaisePropertyChanged(nameof(ShowBackgroundColor));
            this.RaisePropertyChanged(nameof(ShowBackgroundImage));
        }
    }

    public string BackgroundColor
    {
        get => ThemeBackgroundStudio.EditorColor(_draft);
        set
        {
            SetDraft(v => ThemeBackgroundStudio.ApplyEditorColor(_draft, v), value);
            this.RaisePropertyChanged(nameof(BackgroundKind));
            this.RaisePropertyChanged(nameof(ShowBackgroundColor));
            this.RaisePropertyChanged(nameof(ShowBackgroundImage));
        }
    }

    public bool ShowBackgroundColor => ThemeBackgroundStudio.ShowsColorPicker(_draft.BackgroundKind);
    public bool ShowBackgroundImage => ThemeBackgroundStudio.ShowsImagePicker(_draft.BackgroundKind);
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
    private InspectorTarget _inspectorTarget = InspectorTarget.Region;
    private InspectorPanelTab _inspectorTab = InspectorPanelTab.Element;

    public InspectorPanelTab InspectorTab
    {
        get => _inspectorTab;
        set
        {
            this.RaiseAndSetIfChanged(ref _inspectorTab, value);
            NotifyInspectorPanels();
        }
    }

    public bool IsElementTab => _inspectorTab == InspectorPanelTab.Element;
    public bool IsThemeTab => _inspectorTab == InspectorPanelTab.Theme;

    /// <summary>Element properties (text region or shape).</summary>
    public bool ShowElementPanel => IsElementTab && (ShowRegionInspector || ShowShapeInspector);

    /// <summary>Whole-theme properties (background, legibility).</summary>
    public bool ShowThemePanel => IsThemeTab;

    /// <summary>Element panel sections (require both Element tab and matching selection).</summary>
    public bool ShowRegionElementPanel => ShowElementPanel && ShowRegionInspector;
    public bool ShowShapeElementPanel => ShowElementPanel && ShowShapeInspector;
    public bool ShowElementEmptyHint => IsElementTab && !ShowElementPanel;

    private RegionInspectorTab _regionInspectorTab = RegionInspectorTab.Layout;
    private ShapeInspectorTab _shapeInspectorTab = ShapeInspectorTab.Layout;
    private ThemeInspectorTab _themeInspectorTab = ThemeInspectorTab.Background;

    public bool ShowRegionSubTabs => ShowRegionElementPanel;
    public bool ShowShapeSubTabs => ShowShapeElementPanel;
    public bool ShowThemeSubTabs => ShowThemePanel;

    public bool IsRegionLayoutTab => _regionInspectorTab == RegionInspectorTab.Layout;
    public bool IsRegionTextTab => _regionInspectorTab == RegionInspectorTab.Text;
    public bool IsRegionBindingTab => _regionInspectorTab == RegionInspectorTab.Binding;
    public bool IsRegionBoxTab => _regionInspectorTab == RegionInspectorTab.Box;

    public bool IsShapeLayoutTab => _shapeInspectorTab == ShapeInspectorTab.Layout;
    public bool IsShapeFillTab => _shapeInspectorTab == ShapeInspectorTab.Fill;
    public bool IsShapeImageTab => _shapeInspectorTab == ShapeInspectorTab.Image;
    public bool IsShapeArrangeTab => _shapeInspectorTab == ShapeInspectorTab.Arrange;

    public bool IsThemeBackgroundTab => _themeInspectorTab == ThemeInspectorTab.Background;
    public bool IsThemeLegibilityTab => _themeInspectorTab == ThemeInspectorTab.Legibility;

    public bool ShowRegionLayoutPanel => ShowRegionElementPanel && IsRegionLayoutTab;
    public bool ShowRegionTextPanel => ShowRegionElementPanel && IsRegionTextTab;
    public bool ShowRegionBindingPanel => ShowRegionElementPanel && IsRegionBindingTab;
    public bool ShowRegionBoxPanel => ShowRegionElementPanel && IsRegionBoxTab;

    public bool ShowShapeLayoutPanel => ShowShapeElementPanel && IsShapeLayoutTab;
    public bool ShowShapeFillPanel => ShowShapeElementPanel && IsShapeFillTab;
    public bool ShowShapeImagePanel => ShowShapeElementPanel && IsShapeImageTab;
    public bool ShowShapeArrangePanel => ShowShapeElementPanel && IsShapeArrangeTab;

    public bool ShowThemeBackgroundPanel => ShowThemePanel && IsThemeBackgroundTab;
    public bool ShowThemeLegibilityPanel => ShowThemePanel && IsThemeLegibilityTab;

    public void ShowElementInspector() => InspectorTab = InspectorPanelTab.Element;
    public void ShowThemeInspectorTab()
    {
        InspectorTab = InspectorPanelTab.Theme;
        _themeInspectorTab = ThemeInspectorTab.Background;
        NotifyElementSubTabs();
    }

    private void SetRegionInspectorTab(RegionInspectorTab tab)
    {
        _regionInspectorTab = tab;
        NotifyElementSubTabs();
    }

    private void SetShapeInspectorTab(ShapeInspectorTab tab)
    {
        _shapeInspectorTab = tab;
        NotifyElementSubTabs();
    }

    private void SetThemeInspectorTab(ThemeInspectorTab tab)
    {
        _themeInspectorTab = tab;
        NotifyElementSubTabs();
    }

    private void NotifyElementSubTabs()
    {
        foreach (var p in new[]
        {
            nameof(ShowRegionSubTabs), nameof(ShowShapeSubTabs), nameof(ShowThemeSubTabs),
            nameof(IsRegionLayoutTab), nameof(IsRegionTextTab), nameof(IsRegionBindingTab), nameof(IsRegionBoxTab),
            nameof(IsShapeLayoutTab), nameof(IsShapeFillTab), nameof(IsShapeImageTab), nameof(IsShapeArrangeTab),
            nameof(IsThemeBackgroundTab), nameof(IsThemeLegibilityTab),
            nameof(ShowRegionLayoutPanel), nameof(ShowRegionTextPanel), nameof(ShowRegionBindingPanel), nameof(ShowRegionBoxPanel),
            nameof(ShowShapeLayoutPanel), nameof(ShowShapeFillPanel), nameof(ShowShapeImagePanel), nameof(ShowShapeArrangePanel),
            nameof(ShowThemeBackgroundPanel), nameof(ShowThemeLegibilityPanel),
        })
            this.RaisePropertyChanged(p);
    }

    public bool ShowThemeInspector => _inspectorTarget == InspectorTarget.Theme;
    public bool ShowRegionInspector => _inspectorTarget == InspectorTarget.Region;
    public bool ShowShapeInspector => _inspectorTarget == InspectorTarget.Shape;

    /// <summary>Click empty canvas — edit whole-theme settings (background, legibility).</summary>
    public void SelectThemeBackground()
    {
        _selectedShapeIndex = -1;
        this.RaisePropertyChanged(nameof(SelectedShapeIndex));
        _inspectorTarget = InspectorTarget.Theme;
        _inspectorTab = InspectorPanelTab.Theme;
        _themeInspectorTab = ThemeInspectorTab.Background;
        NotifyInspectorTarget();
        NotifyInspectorPanels();
        RaiseRegionSelection();
        _syncingSelection = true;
        _selectedObject = null;
        this.RaisePropertyChanged(nameof(SelectedObject));
        _syncingSelection = false;
    }

    private void NotifyInspectorPanels()
    {
        this.RaisePropertyChanged(nameof(IsElementTab));
        this.RaisePropertyChanged(nameof(IsThemeTab));
        this.RaisePropertyChanged(nameof(ShowElementPanel));
        this.RaisePropertyChanged(nameof(ShowThemePanel));
        this.RaisePropertyChanged(nameof(ShowRegionElementPanel));
        this.RaisePropertyChanged(nameof(ShowShapeElementPanel));
        this.RaisePropertyChanged(nameof(ShowElementEmptyHint));
        NotifyElementSubTabs();
    }

    private void NotifyInspectorTarget()
    {
        this.RaisePropertyChanged(nameof(ShowThemeInspector));
        this.RaisePropertyChanged(nameof(HasSelectionHandles));
        this.RaisePropertyChanged(nameof(ShowRegionInspector));
        this.RaisePropertyChanged(nameof(ShowShapeInspector));
        this.RaisePropertyChanged(nameof(SelObjectName));
        this.RaisePropertyChanged(nameof(IsShapeSelected));
        this.RaisePropertyChanged(nameof(IsRegionSelected));
        NotifyInspectorPanels();
    }

    public RegionKind SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (TryGetRegion(value) is null) return;
            this.RaiseAndSetIfChanged(ref _selectedRegion, value);
            // Picking a text region clears any shape selection.
            if (_selectedShapeIndex != -1) { _selectedShapeIndex = -1; this.RaisePropertyChanged(nameof(SelectedShapeIndex)); }
            _inspectorTarget = InspectorTarget.Region;
            _inspectorTab = InspectorPanelTab.Element;
            _regionInspectorTab = RegionInspectorTab.Layout;
            NotifyInspectorTarget();
            NotifyInspectorPanels();
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
        if (_draft.TitleRegion is not null)
            LayoutObjects.Add(new LayoutObjectItem { Name = "Title", Region = RegionKind.Title });
        if (_draft.BodyRegion is not null)
            LayoutObjects.Add(new LayoutObjectItem { Name = "Body", Region = RegionKind.Body });
        if (_draft.FooterRegion is not null)
            LayoutObjects.Add(new LayoutObjectItem { Name = "Footer", Region = RegionKind.Footer });
        for (var i = 0; i < _draft.Shapes.Count; i++)
        {
            var idx = i;
            LayoutObjects.Add(new LayoutObjectItem
            {
                Name = ShapeLabel(idx),
                IsShape = true,
                ShapeIndex = idx,
                CanRename = true,
                RenameAction = (item, newName) => RenameShape(item.ShapeIndex, newName),
            });
        }
        RebuildShapeHandles();
        SyncSelectedObject();
        this.RaisePropertyChanged(nameof(CanAddTitleLayer));
        this.RaisePropertyChanged(nameof(CanAddBodyLayer));
        this.RaisePropertyChanged(nameof(CanAddFooterLayer));
    }

    /// <summary>The label shown for a shape: the operator's name if set, otherwise an auto label
    /// ("Image" / "Bar" / "Rectangle").</summary>
    private string ShapeLabel(int index)
    {
        var s = _draft.Shapes[index];
        return string.IsNullOrWhiteSpace(s.Name) ? ThemeLayerNaming.DefaultLabel(s) : s.Name!;
    }

    /// <summary>Renames a shape layer (blank clears back to the auto label). Persisted on the draft.</summary>
    private void RenameShape(int index, string? newName)
    {
        if (index < 0 || index >= _draft.Shapes.Count) return;
        var trimmed = newName?.Trim();
        _draft.Shapes[index].Name = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        RebuildLayoutObjects();
        this.RaisePropertyChanged(nameof(SelObjectName));
        if (!_loading) RefreshPreview();
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

    /// <summary>Editor-space, transparent hit-targets drawn over each shape so any object can be
    /// clicked directly on the canvas (free-form selection), not just from the OBJECTS list.</summary>
    public ObservableCollection<ShapeHandleItem> ShapeHandles { get; } = [];

    private void RebuildShapeHandles()
    {
        ShapeHandles.Clear();
        for (var i = 0; i < _draft.Shapes.Count; i++)
            ShapeHandles.Add(new ShapeHandleItem(i, _draft.Shapes[i], EditorScale));
    }

    /// <summary>Selects a shape directly (used when its hit-target is clicked on the canvas).</summary>
    public void SelectShape(int index)
    {
        if (index < 0 || index >= _draft.Shapes.Count) return;
        SelectedShapeIndex = index;
    }

    private int _selectedShapeIndex = -1;
    public int SelectedShapeIndex
    {
        get => _selectedShapeIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedShapeIndex, value);
            if (value >= 0) _inspectorTarget = InspectorTarget.Shape;
            if (value >= 0) _inspectorTab = InspectorPanelTab.Element;
            RaiseRegionSelection();
            RaiseSelectedRegionProps();
            if (value >= 0)
            {
                _shapeInspectorTab = ShapeInspectorTab.Layout;
                NotifyInspectorTarget();
                NotifyInspectorPanels();
            }
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

    /// <summary>Deletes whatever object is selected. Shapes are removed outright; text layers are removed from the layout.</summary>
    private void DeleteSelected()
    {
        if (ShapeMode)
        {
            RemoveShape();
            return;
        }

        switch (_selectedRegion)
        {
            case RegionKind.Title: _draft.TitleRegion = null; break;
            case RegionKind.Footer: _draft.FooterRegion = null; break;
            default: _draft.BodyRegion = null; break;
        }

        SelectFirstAvailableRegion();
        AfterRegionEdit();
        RebuildLayoutObjects();
    }

    private void AddTextLayer(RegionKind kind)
    {
        var region = CreateRegionFromSlot(kind);
        switch (kind)
        {
            case RegionKind.Title: _draft.TitleRegion = region; break;
            case RegionKind.Footer: _draft.FooterRegion = region; break;
            default: _draft.BodyRegion = region; break;
        }

        _inspectorTarget = InspectorTarget.Region;
        SelectedRegion = kind;
        RebuildLayoutObjects();
        if (!_loading) RefreshPreview();
    }

    private void SelectFirstAvailableRegion()
    {
        if (_draft.BodyRegion is not null) SelectedRegion = RegionKind.Body;
        else if (_draft.TitleRegion is not null) SelectedRegion = RegionKind.Title;
        else if (_draft.FooterRegion is not null) SelectedRegion = RegionKind.Footer;
        else
        {
            _inspectorTarget = InspectorTarget.Theme;
            _inspectorTab = InspectorPanelTab.Theme;
            NotifyInspectorTarget();
            NotifyInspectorPanels();
        }
    }

    private ThemeRegion CreateRegionFromSlot(RegionKind kind)
    {
        var (t, b, f) = _draft.ResolveRegions();
        var template = kind switch
        {
            RegionKind.Title => t,
            RegionKind.Footer => f,
            _ => b,
        };
        var region = template.Clone();
        region.Visible = true;
        region.ApplyDefaultContentBindings(MapSlot(kind));
        return region;
    }

    private static ThemeTextSlot MapSlot(RegionKind kind) => kind switch
    {
        RegionKind.Title => ThemeTextSlot.Title,
        RegionKind.Footer => ThemeTextSlot.Footer,
        _ => ThemeTextSlot.Body,
    };

    private static void ApplyDefaultBindings(ThemeRegion region, RegionKind kind)
        => region.ApplyDefaultContentBindings(MapSlot(kind));

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

    private static readonly ThemeRegion NoRegion = new();

    private ThemeRegion CurRegion => TryGetRegion(_selectedRegion) ?? NoRegion;

    private bool HasGeometryTarget => ShapeMode || TryGetRegion(_selectedRegion) is not null;

    public bool HasSelectionHandles => !ShowThemeInspector && HasGeometryTarget;

    private ThemeRegion? TryGetRegion(RegionKind kind) => kind switch
    {
        RegionKind.Title => _draft.TitleRegion,
        RegionKind.Footer => _draft.FooterRegion,
        _ => _draft.BodyRegion,
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
    public double SelX { get => GeoX; set { GeoX = ThemePlacement.ClampPosition(value, GeoY, GeoW, GeoH, Theme.CanvasWidth, Theme.CanvasHeight, allowBleed: ShapeMode).X; AfterRegionEdit(); } }
    public double SelY { get => GeoY; set { GeoY = ThemePlacement.ClampPosition(GeoX, value, GeoW, GeoH, Theme.CanvasWidth, Theme.CanvasHeight, allowBleed: ShapeMode).Y; AfterRegionEdit(); } }
    public double SelWidth { get => GeoW; set { GeoW = Clamp(value, 20, Theme.CanvasWidth - GeoX); AfterRegionEdit(); } }
    public double SelHeight { get => GeoH; set { GeoH = Clamp(value, 20, Theme.CanvasHeight - GeoY); AfterRegionEdit(); } }
    public bool SelVisible
    {
        get => ShapeMode || (TryGetRegion(_selectedRegion)?.Visible ?? false);
        set
        {
            if (ShapeMode || TryGetRegion(_selectedRegion) is not { } region) return;
            region.Visible = value;
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
    public bool IsShapeSelected => _inspectorTarget == InspectorTarget.Shape;
    public bool IsRegionSelected => _inspectorTarget == InspectorTarget.Region;
    public string SelObjectName => _inspectorTarget switch
    {
        InspectorTarget.Theme => "Theme",
        InspectorTarget.Shape => ShapeLabel(_selectedShapeIndex),
        _ => _selectedRegion.ToString(),
    };
    public string SelShapeColor { get => CurShape?.Color ?? "#80FFFFFF"; set { if (CurShape is not null) { CurShape.Color = value; AfterRegionEdit(); } } }
    public double SelShapeCorner { get => CurShape?.CornerRadius ?? 0; set { if (CurShape is not null) { CurShape.CornerRadius = value; AfterRegionEdit(); } } }
    public double SelShapeOpacity { get => CurShape?.Opacity ?? 1.0; set { if (CurShape is not null) { CurShape.Opacity = value; AfterRegionEdit(); } } }
    public string? SelShapeImagePath { get => CurShape?.ImagePath; set { if (CurShape is not null) { CurShape.ImagePath = value; AfterRegionEdit(); } } }
    public ThemeImageFit SelShapeImageFit { get => CurShape?.ImageFit ?? ThemeImageFit.UniformToFill; set { if (CurShape is not null) { CurShape.ImageFit = value; AfterRegionEdit(); } } }
    public double SelShapeImageOffsetX { get => CurShape?.ImageOffsetX ?? 0; set { if (CurShape is not null) { CurShape.ImageOffsetX = value; AfterRegionEdit(); } } }
    public double SelShapeImageOffsetY { get => CurShape?.ImageOffsetY ?? 0; set { if (CurShape is not null) { CurShape.ImageOffsetY = value; AfterRegionEdit(); } } }
    public double SelShapeImageZoom { get => CurShape?.ImageZoom ?? 1.0; set { if (CurShape is not null) { CurShape.ImageZoom = value; AfterRegionEdit(); } } }
    public bool SelShapeUseLiveBackground { get => CurShape?.UseLiveBackground ?? false; set { if (CurShape is not null) { CurShape.UseLiveBackground = value; AfterRegionEdit(); } } }

    public ThemeContentField SelScriptureField { get => CurRegion.ScriptureField; set { CurRegion.ScriptureField = value; AfterRegionEdit(); } }
    public ThemeContentField SelSongField { get => CurRegion.SongField; set { CurRegion.SongField = value; AfterRegionEdit(); } }
    public ThemeContentField SelNoteField { get => CurRegion.NoteField; set { CurRegion.NoteField = value; AfterRegionEdit(); } }
    public ThemeContentField SelAnnouncementField { get => CurRegion.AnnouncementField; set { CurRegion.AnnouncementField = value; AfterRegionEdit(); } }

    // Editor-space rectangles for drawing the three boxes.
    public double TitleBoxX => (_draft.TitleRegion?.X ?? 0) * EditorScale;
    public double TitleBoxY => (_draft.TitleRegion?.Y ?? 0) * EditorScale;
    public double TitleBoxW => (_draft.TitleRegion?.Width ?? 0) * EditorScale;
    public double TitleBoxH => (_draft.TitleRegion?.Height ?? 0) * EditorScale;
    public bool TitleBoxVisible => _draft.TitleRegion is not null;
    public bool IsTitleSelected => _selectedRegion == RegionKind.Title;

    public double BodyBoxX => (_draft.BodyRegion?.X ?? 0) * EditorScale;
    public double BodyBoxY => (_draft.BodyRegion?.Y ?? 0) * EditorScale;
    public double BodyBoxW => (_draft.BodyRegion?.Width ?? 0) * EditorScale;
    public double BodyBoxH => (_draft.BodyRegion?.Height ?? 0) * EditorScale;
    public bool BodyBoxVisible => _draft.BodyRegion is not null;
    public bool IsBodySelected => _selectedRegion == RegionKind.Body;

    public double FooterBoxX => (_draft.FooterRegion?.X ?? 0) * EditorScale;
    public double FooterBoxY => (_draft.FooterRegion?.Y ?? 0) * EditorScale;
    public double FooterBoxW => (_draft.FooterRegion?.Width ?? 0) * EditorScale;
    public double FooterBoxH => (_draft.FooterRegion?.Height ?? 0) * EditorScale;
    public bool FooterBoxVisible => _draft.FooterRegion is not null;
    public bool IsFooterSelected => _selectedRegion == RegionKind.Footer;

    // Selection overlay (the box that has the drag/resize handles).
    public double SelBoxX => GeoX * EditorScale;
    public double SelBoxY => GeoY * EditorScale;
    public double SelBoxW => GeoW * EditorScale;
    public double SelBoxH => GeoH * EditorScale;

    /// <summary>Live "width × height" readout (design px) shown beside the selection box.</summary>
    public string SelSizeLabel => $"{Math.Round(GeoW)} × {Math.Round(GeoH)}";

    /// <summary>Drag the selected target by an editor-space delta (from the move thumb).</summary>
    public void MoveSelected(double dxEditor, double dyEditor)
    {
        if (!HasGeometryTarget) return;
        // Shapes/images may bleed past the frame (so a full-frame imported lower-third can be nudged
        // into place); text regions stay fully inside. See ThemePlacement for the rules.
        var (nx, ny) = ThemePlacement.ClampPosition(
            GeoX + dxEditor / EditorScale, GeoY + dyEditor / EditorScale,
            GeoW, GeoH, Theme.CanvasWidth, Theme.CanvasHeight, allowBleed: ShapeMode);
        GeoX = nx;
        GeoY = ny;
        AfterRegionEdit();
    }

    /// <summary>Resize the selected target from a handle ("tl","t","tr","l","r","bl","b","br").</summary>
    public void ResizeSelected(string handle, double dxEditor, double dyEditor)
    {
        if (!HasGeometryTarget) return;
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
        ClearSaveFeedback();
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
            nameof(SelBoxX), nameof(SelBoxY), nameof(SelBoxW), nameof(SelBoxH), nameof(SelSizeLabel),
            nameof(HasSelectionHandles),
            nameof(IsShapeSelected), nameof(IsRegionSelected),
            nameof(SelShapeColor), nameof(SelShapeCorner), nameof(SelShapeOpacity),
            nameof(SelShapeImagePath), nameof(SelShapeImageFit),
            nameof(SelShapeImageOffsetX), nameof(SelShapeImageOffsetY), nameof(SelShapeImageZoom), nameof(SelShapeUseLiveBackground),
            nameof(SelRegionBgColor), nameof(SelRegionCorner), nameof(SelRegionBgImagePath), nameof(SelRegionBgImageFit),
            nameof(SelRegionImageOffsetX), nameof(SelRegionImageOffsetY), nameof(SelRegionImageZoom), nameof(SelRegionUseLiveBackground),
            nameof(SelTextPaddingX), nameof(SelTextPaddingY),
            nameof(SelFontSize), nameof(SelFontFamily), nameof(SelTextColor), nameof(SelObjectName),
            nameof(SelScriptureField), nameof(SelSongField), nameof(SelNoteField), nameof(SelAnnouncementField),
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

        // Keep the shape hit-targets glued to their shapes as they move/resize or the canvas rescales.
        foreach (var h in ShapeHandles) h.Refresh(EditorScale);
    }

    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, Math.Max(lo, v)));

    /// <summary>Materializes the draft's layout regions so the editor always has concrete boxes
    /// to manipulate. Older/auto themes derive them from padding; once edited + saved they're explicit
    /// — including an empty layout with every text layer deleted.</summary>
    private void EnsureDraftRegions() => _draft.EnsureEditorRegions();

    private static void EnsureRegionBindings(ThemeRegion? region, RegionKind kind)
    {
        if (region is null) return;
        if (region.ScriptureField != ThemeContentField.None
            || region.SongField != ThemeContentField.None
            || region.NoteField != ThemeContentField.None
            || region.AnnouncementField != ThemeContentField.None)
            return;
        ApplyDefaultBindings(region, kind);
    }

    private void SetDraft<T>(Action<T> apply, T value, [CallerMemberName] string? name = null)
    {
        ClearSaveFeedback();
        apply(value);
        this.RaisePropertyChanged(name!);
        if (!_loading) RefreshPreview();
    }

    private void LoadDraft(Theme theme)
    {
        _loading = true;
        _draft = theme.Clone();
        EnsureDraftRegions();
        EnsureRegionBindings(_draft.TitleRegion, RegionKind.Title);
        EnsureRegionBindings(_draft.BodyRegion, RegionKind.Body);
        EnsureRegionBindings(_draft.FooterRegion, RegionKind.Footer);

        Shapes.Clear();
        foreach (var s in _draft.Shapes) Shapes.Add(s);
        _selectedShapeIndex = -1;
        this.RaisePropertyChanged(nameof(SelectedShapeIndex));
        RebuildLayoutObjects();

        _editingOriginalName = theme.Name;
        _selectedThemeName = theme.Name;
        SelectFirstAvailableRegion();
        this.RaisePropertyChanged(nameof(SelectedRegion));
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
        var (title, body, footer) = _previewMode switch
        {
            PreviewContentMode.Song => ("Amazing Grace", "Amazing grace, how sweet the sound\nThat saved a wretch like me", ""),
            PreviewContentMode.Note => ("Opening Prayer", "Heavenly Father, we gather today thankful for your presence among us.", ""),
            PreviewContentMode.Announcement => ("Welcome", "Join us for coffee and fellowship after the service in the fellowship hall.", ""),
            _ => ("John 3:16", "For God so loved the world that he gave his one and only Son, that whoever believes in him shall not perish but have eternal life.", $"John 3:16 — {_selectedThemeName ?? Name}"),
        };

        Preview.SetSampleContent(title, body, footer);
        Preview.SetPreviewSlideType(MapPreviewMode(_previewMode));
        Preview.PreviewTheme(_draft);
    }

    private static SlideType MapPreviewMode(PreviewContentMode mode) => mode switch
    {
        PreviewContentMode.Song => SlideType.Lyric,
        PreviewContentMode.Note => SlideType.Note,
        PreviewContentMode.Announcement => SlideType.Announcement,
        _ => SlideType.Scripture,
    };

    private void RefreshThemeNames()
    {
        _syncingThemeList = true;
        try
        {
            var keep = _selectedThemeName;
            ThemeNames.Clear();
            foreach (var t in _themes.Themes)
                ThemeNames.Add(t.Name);
            _selectedThemeName = keep;
            this.RaisePropertyChanged(nameof(SelectedThemeName));
            this.RaisePropertyChanged(nameof(CanDeleteTheme));
        }
        finally
        {
            _syncingThemeList = false;
        }
    }

    private async Task NewThemeAsync()
    {
        var name = UniqueName("New Theme");
        var theme = new Theme { Name = name, UsesLayerEditor = true };
        PopulateDefaultRegions(theme);
        await _themes.AddOrUpdateAsync(theme);
        RefreshThemeNames();
        SelectedThemeName = name;
    }

    private static void PopulateDefaultRegions(Theme theme)
    {
        var (t, b, f) = theme.ResolveRegions();
        theme.TitleRegion = t.Clone();
        theme.BodyRegion = b.Clone();
        theme.FooterRegion = f.Clone();
        theme.TitleRegion.ApplyDefaultContentBindings(ThemeTextSlot.Title);
        theme.BodyRegion.ApplyDefaultContentBindings(ThemeTextSlot.Body);
        theme.FooterRegion.ApplyDefaultContentBindings(ThemeTextSlot.Footer);
    }

    private async Task DuplicateThemeAsync()
    {
        var copy = _draft.Clone();
        copy.Name = UniqueName($"{_draft.Name} copy");
        await _themes.AddOrUpdateAsync(copy);
        RefreshThemeNames();
        SelectedThemeName = copy.Name;
    }

    /// <summary>True once an asset store is available, so the view can enable/disable the import button.</summary>
    public bool CanImportDesign => _assetStore is not null;

    /// <summary>
    /// Imports a church's designed lower-third graphic as a new theme. The picked file is copied into
    /// the app's asset store (so the theme stays self-contained), then <see cref="ThemeImporter"/>
    /// lays the graphic onto the 1920x1080 frame at full width / bottom-anchored with a green ATEM-key
    /// background and seeded text regions. The new theme is saved and selected for fine-tuning.
    /// </summary>
    /// <param name="sourcePath">The image the operator picked.</param>
    /// <param name="pixelWidth">Source image width in pixels (from the loaded bitmap).</param>
    /// <param name="pixelHeight">Source image height in pixels.</param>
    public async Task ImportDesignAsync(string sourcePath, int pixelWidth, int pixelHeight)
    {
        if (_assetStore is null || string.IsNullOrWhiteSpace(sourcePath)) return;

        var storedPath = _assetStore.Save(sourcePath);
        var baseName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        var name = UniqueName(string.IsNullOrWhiteSpace(baseName) ? "Imported design" : $"{baseName} (imported)");

        var theme = ThemeImporter.FromImage(name, storedPath, pixelWidth, pixelHeight);
        await _themes.AddOrUpdateAsync(theme);

        RefreshThemeNames();
        SelectedThemeName = name; // loads the draft + refreshes the live preview
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
        _saveStatusCts?.Cancel();
        ShowSaveSuccess = false;
        IsSaving = true;

        try
        {
            if (string.IsNullOrWhiteSpace(_draft.Name))
                _draft.Name = UniqueName("Theme");

            _draft.UsesLayerEditor = true;
            await _themes.AddOrUpdateAsync(_draft.Clone(), _editingOriginalName);

            foreach (SlideType st in Enum.GetValues<SlideType>())
                await _themes.SetAssignmentAsync(st, _draft.Name);

            _editingOriginalName = _draft.Name;
            RefreshThemeNames();
            _selectedThemeName = _draft.Name;
            this.RaisePropertyChanged(nameof(SelectedThemeName));
            RefreshPreview();

            ShowSaveSuccess = true;
            _saveStatusCts = new CancellationTokenSource();
            _ = ResetSaveSuccessAfterDelayAsync(_saveStatusCts.Token);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ResetSaveSuccessAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(3000, token);
            ShowSaveSuccess = false;
        }
        catch (TaskCanceledException)
        {
            // Cleared early because the draft changed.
        }
    }

    private void ClearSaveFeedback()
    {
        if (_loading || (!ShowSaveSuccess && !IsSaving)) return;
        _saveStatusCts?.Cancel();
        ShowSaveSuccess = false;
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
            nameof(Name), nameof(FontFamilyName), nameof(TitleFontFamily), nameof(FooterFontFamily),
            nameof(SelFontFamily), nameof(BodyFontSize), nameof(TitleFontSize), nameof(FooterFontSize),
            nameof(Bold), nameof(LineHeightMultiplier), nameof(TextAlign), nameof(TextColor), nameof(TitleColor),
            nameof(FooterColor), nameof(PaddingHorizontal), nameof(PaddingVertical), nameof(Layout), nameof(BackgroundKind),
            nameof(BackgroundColor), nameof(ShowBackgroundColor), nameof(ShowBackgroundImage),
            nameof(BackgroundImagePath), nameof(OutlineEnabled), nameof(OutlineColor),
            nameof(OutlineWidth), nameof(ShadowEnabled), nameof(ShadowColor), nameof(ShadowBlur),
            nameof(ShadowOffsetX), nameof(ShadowOffsetY), nameof(ShadowOpacity), nameof(ImageFit), nameof(LowerThirdBarColor),
        })
        {
            this.RaisePropertyChanged(p);
        }
    }

    public void Dispose()
    {
        _saveStatusCts?.Cancel();
        _saveStatusCts?.Dispose();
        Preview.Dispose();
    }
}

/// <summary>Which inspector panel set is active in Theme Studio.</summary>
public enum InspectorTarget { Theme, Region, Shape }

/// <summary>Top inspector tabs (ProPresenter-style Element vs Theme).</summary>
public enum InspectorPanelTab { Element, Theme }

/// <summary>Sub-tabs for a selected text region in the Element inspector.</summary>
public enum RegionInspectorTab { Layout, Text, Binding, Box }

/// <summary>Sub-tabs for a selected shape in the Element inspector.</summary>
public enum ShapeInspectorTab { Layout, Fill, Image, Arrange }

/// <summary>Sub-tabs for whole-theme settings.</summary>
public enum ThemeInspectorTab { Background, Legibility }

/// <summary>Which positionable text box the Layout editor is currently editing.</summary>
public enum RegionKind { Title, Body, Footer }

/// <summary>Sample content type shown in the Theme Studio preview canvas.</summary>
public enum PreviewContentMode { Scripture, Song, Note, Announcement }

/// <summary>An entry in the studio's unified object list (a text region or a shape).</summary>
public sealed class LayoutObjectItem : ViewModelBase
{
    public string Name { get; init; } = "";
    public bool IsShape { get; init; }
    public int ShapeIndex { get; init; } = -1;
    public RegionKind Region { get; init; }

    /// <summary>Only shapes can be renamed; the three text regions keep their fixed names.</summary>
    public bool CanRename { get; init; }

    /// <summary>Invoked with the new name when an inline rename is committed.</summary>
    public Action<LayoutObjectItem, string>? RenameAction { get; init; }

    public string Display => Name;

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }

    private string _editName = "";
    public string EditName
    {
        get => _editName;
        set => this.RaiseAndSetIfChanged(ref _editName, value);
    }

    /// <summary>Enter inline-edit mode (double-click). No-op for non-renamable rows.</summary>
    public void BeginEdit()
    {
        if (!CanRename) return;
        EditName = Name;
        IsEditing = true;
    }

    /// <summary>Commit the typed name back to the model (via <see cref="RenameAction"/>).</summary>
    public void CommitEdit()
    {
        if (!IsEditing) return;
        IsEditing = false;
        RenameAction?.Invoke(this, EditName);
    }

    /// <summary>Abandon the edit without changing the name.</summary>
    public void CancelEdit() => IsEditing = false;
}

/// <summary>A transparent, editor-space rectangle laid over a shape so it can be clicked directly on
/// the canvas. Geometry mirrors the underlying <see cref="ThemeShape"/> scaled to editor space.</summary>
public sealed class ShapeHandleItem(int index, ThemeShape shape, double scale) : ViewModelBase
{
    private double _scale = scale;

    public int Index { get; } = index;

    public double EX => shape.X * _scale;
    public double EY => shape.Y * _scale;
    public double EW => shape.Width * _scale;
    public double EH => shape.Height * _scale;

    /// <summary>Re-reads the shape geometry at the given editor scale and notifies the canvas.</summary>
    public void Refresh(double newScale)
    {
        _scale = newScale;
        this.RaisePropertyChanged(nameof(EX));
        this.RaisePropertyChanged(nameof(EY));
        this.RaisePropertyChanged(nameof(EW));
        this.RaisePropertyChanged(nameof(EH));
    }
}
