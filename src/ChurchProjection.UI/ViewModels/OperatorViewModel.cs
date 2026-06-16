using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Projection;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Parsing;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Bible;
using ChurchProjection.Infrastructure.Data;
using ChurchProjection.UI.Services;
using ChurchProjection.UI.ViewModels.Operator;
using ChurchProjection.UI.ViewModels.Planning;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels;

public class OperatorViewModel : ViewModelBase, IActivatableViewModel
{
    private const int SuggestionsTabIndex = 1;
    private const int TopicalTabIndex = 2;
    private const int SongsTabIndex = 3;
    private static readonly TimeSpan PrewarmCacheWait = TimeSpan.FromSeconds(2);

    private readonly IProjectionService _projection;
    private readonly SettingsRepository _settings;
    private readonly SerialDisposable _feedSub = new();
    private readonly IAiMatcherService _aiMatcher;
    private readonly BibleCacheService _bibleCache;
    private readonly IThemeService _themes;
    private readonly IScriptureSearchService _scriptureSearch;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IContentLibraryService _contentLibrary;
    private readonly IProPresenterService _proPresenter;
    private readonly ILiveBackgroundService _liveBackground;
    private readonly IAnnouncementService _announcements;
    private readonly ILayerService _layers;
    private readonly Progress<string> _indexProgress;

    // The scripture reference currently shown live, so a translation switch can re-render it.
    private ScriptureReference? _liveScriptureRef;

    // The content/suggestion item currently highlighted as live in the operator UI, so we can
    // clear its ring when a different item goes live.
    private ContentItem? _liveContentItem;
    private SuggestionItem? _liveSuggestionItem;

    // The slide type currently on the live output, so the program-preview theme picker knows which
    // per-type assignment to retarget when the operator switches themes.
    private SlideType _liveSlideType = SlideType.Blank;

    private string _liveTitle = string.Empty;
    private string _liveBody = string.Empty;
    private string _liveFooter = string.Empty;
    private bool _isLive;

    private string _previewTitle = string.Empty;
    private string _previewBody = string.Empty;
    private string _previewFooter = string.Empty;
    private bool _hasPreview;

    private string _statusText = "Ready";
    private string _syncStatus = "Local only";
    private string _deckPositionText = string.Empty;
    private bool _hasMultipleSlides;
    private DisplayOption? _selectedDisplay;
    private bool _singleClickGoesLive;
    private int _selectedContentTab;
    private int _aiSuggestionCount;
    private bool _hasNewSuggestions;
    private bool _hasNewTopical;
    private string _projectorFontSize = "Large";
    private string _projectorBackground = "Black";
    private string _projectorLayout = "Full Screen";
    private bool _autoStartListening;
    private bool _screenOutputEnabled = true;
    private double _previewWidth = 1920;
    private double _previewHeight = 1080;

    public ViewModelActivator Activator { get; } = new();

    /// <summary>
    /// Master kill-switch for the app's own projector screen output(s). When off, the projector
    /// windows are hidden so the physical screen reverts to the desktop / another source (e.g. a
    /// media server or ProPresenter), without changing each output's configured channel. This is
    /// distinct from Blank, which keeps the feed live but shows black.
    /// </summary>
    public bool ScreenOutputEnabled
    {
        get => _screenOutputEnabled;
        set => this.RaiseAndSetIfChanged(ref _screenOutputEnabled, value);
    }

    public ContentSearchViewModel ContentSearch { get; }
    public SongImportViewModel SongImport { get; }
    public ServiceQueueViewModel ServiceQueue { get; }
    public TranscriptionViewModel Transcription { get; }
    public TopicalSearchViewModel TopicalSearch { get; }
    public SongSearchViewModel SongSearch { get; }
    public ProPresenterViewModel ProPresenter { get; }

    /// <summary>The swappable background media palette (still images + motion loops).</summary>
    public BackgroundsViewModel Backgrounds { get; }

    /// <summary>The Media Playback bin: full-screen / lower-third graphics and videos sent live to all screens or one.</summary>
    public Operator.MediaPlaybackViewModel MediaPlayback { get; }

    private ProjectorViewModel _programPreview = null!;
    /// <summary>A live, themed thumbnail of the program feed, shown in the Program monitor
    /// so the operator sees exactly what is on screen (theme background, fonts and colors included).</summary>
    public ProjectorViewModel ProgramPreview
    {
        get => _programPreview;
        private set => this.RaiseAndSetIfChanged(ref _programPreview, value);
    }

    /// <summary>The pixel dimensions of the output feeding the active channel, so the Program
    /// thumbnail matches the real screen's aspect ratio (e.g. 1920×1200 = 16:10, not 16:9).</summary>
    public double PreviewWidth
    {
        get => _previewWidth;
        private set => this.RaiseAndSetIfChanged(ref _previewWidth, value);
    }

    public double PreviewHeight
    {
        get => _previewHeight;
        private set => this.RaiseAndSetIfChanged(ref _previewHeight, value);
    }

    /// <summary>Creates a fresh Theme Studio view model bound to the shared theme service.</summary>
    public ThemeStudioViewModel CreateThemeStudio() => new(_themes, _liveBackground);

    /// <summary>Creates a fresh song editor (new song). Wire <see cref="SongEditorViewModel.Saved"/> to refresh the library.</summary>
    public SongEditorViewModel CreateSongEditor() => new(_contentLibrary, _themes);

    /// <summary>Reloads the library/search index after a song is saved from the editor.</summary>
    public Task RefreshLibraryAsync() => ReloadLibraryAsync();

    public string LiveTitle
    {
        get => _liveTitle;
        set => this.RaiseAndSetIfChanged(ref _liveTitle, value);
    }

    public string LiveBody
    {
        get => _liveBody;
        set => this.RaiseAndSetIfChanged(ref _liveBody, value);
    }

    public string LiveFooter
    {
        get => _liveFooter;
        set => this.RaiseAndSetIfChanged(ref _liveFooter, value);
    }

    public bool IsLive
    {
        get => _isLive;
        set => this.RaiseAndSetIfChanged(ref _isLive, value);
    }

    public string PreviewTitle
    {
        get => _previewTitle;
        set => this.RaiseAndSetIfChanged(ref _previewTitle, value);
    }

    public string PreviewBody
    {
        get => _previewBody;
        set => this.RaiseAndSetIfChanged(ref _previewBody, value);
    }

    public string PreviewFooter
    {
        get => _previewFooter;
        set => this.RaiseAndSetIfChanged(ref _previewFooter, value);
    }

    public bool HasPreview
    {
        get => _hasPreview;
        set => this.RaiseAndSetIfChanged(ref _hasPreview, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string DeckPositionText
    {
        get => _deckPositionText;
        set => this.RaiseAndSetIfChanged(ref _deckPositionText, value);
    }

    public bool HasMultipleSlides
    {
        get => _hasMultipleSlides;
        set => this.RaiseAndSetIfChanged(ref _hasMultipleSlides, value);
    }

    public string SyncStatus
    {
        get => _syncStatus;
        set => this.RaiseAndSetIfChanged(ref _syncStatus, value);
    }

    private string _accountLabel = "Local library";
    /// <summary>Signed-in organization / branch shown in the top bar.</summary>
    public string AccountLabel
    {
        get => _accountLabel;
        set => this.RaiseAndSetIfChanged(ref _accountLabel, value);
    }

    /// <summary>Raised when the operator chooses Sign out; the app layer clears the session and re-gates.</summary>
    public event Action? SignOutRequested;

    /// <summary>Updates the displayed account/seat status after sign-in.</summary>
    public void SetAccount(string organizationName, string branchName, int seatsUsed, int seatCount)
    {
        if (string.IsNullOrWhiteSpace(organizationName))
        {
            AccountLabel = "Local library";
            return;
        }

        var label = string.IsNullOrWhiteSpace(branchName)
            ? organizationName
            : $"{organizationName} · {branchName}";

        // Seat usage rides along with the account label; SyncStatus is reserved for live sync state.
        AccountLabel = seatCount > 0 ? $"{label} · Seat {seatsUsed}/{seatCount}" : label;
    }

    /// <summary>Maps the scheduler's state to the short label shown next to the account.</summary>
    private static string DescribeSync(SyncStatusInfo info) => info.State switch
    {
        SyncState.Disabled => "Local only",
        SyncState.Syncing => "Syncing…",
        SyncState.Offline => "Offline — will sync",
        SyncState.Error => "Sync error",
        SyncState.Idle => info.LastSyncUtc is { } t ? $"Synced {t.ToLocalTime():HH:mm}" : "Connected",
        _ => "",
    };

    /// <summary>Available projector output targets, populated by the window layer at startup.</summary>
    public ObservableCollection<DisplayOption> AvailableDisplays { get; } = [];

    /// <summary>The screen (or windowed preview) the projector output is currently sent to.</summary>
    public DisplayOption? SelectedDisplay
    {
        get => _selectedDisplay;
        set => this.RaiseAndSetIfChanged(ref _selectedDisplay, value);
    }

    /// <summary>The screens (physical displays, windowed preview, ProPresenter); built by the window layer.</summary>
    public ObservableCollection<OutputRow> Outputs { get; } = [];

    /// <summary>Theme names for the per-output "view" picker; first entry follows the content assignment.</summary>
    public ObservableCollection<string> ThemeOptions { get; } = [];

    /// <summary>Plain theme names for the program-preview quick theme switcher (no "follow content").</summary>
    public ObservableCollection<string> LiveThemeOptions { get; } = [];

    /// <summary>
    /// Theme applied to whatever is currently live. Setting it retargets the theme assignment for the
    /// live slide's type (e.g. all scripture), persists it, and re-renders the live output + preview
    /// immediately via the theme service's Changed signal.
    /// </summary>
    public string LiveThemeName
    {
        get => _themes.GetAssignment(_liveSlideType);
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == _themes.GetAssignment(_liveSlideType)) return;
            _ = _themes.SetAssignmentAsync(_liveSlideType, value);
            this.RaisePropertyChanged();
            StatusText = $"Theme for {_liveSlideType} → {value}";
        }
    }

    public bool SingleClickGoesLive
    {
        get => _singleClickGoesLive;
        set
        {
            this.RaiseAndSetIfChanged(ref _singleClickGoesLive, value);
            _ = _settings.SetBoolAsync("single_click_goes_live", value);
        }
    }

    public string ProjectorFontSize
    {
        get => _projectorFontSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _projectorFontSize, value);
            _ = _settings.SetAsync("projector_font_size", value);
        }
    }

    public string ProjectorBackground
    {
        get => _projectorBackground;
        set
        {
            this.RaiseAndSetIfChanged(ref _projectorBackground, value);
            _ = _settings.SetAsync("projector_background", value);
        }
    }

    public string ProjectorLayout
    {
        get => _projectorLayout;
        set
        {
            this.RaiseAndSetIfChanged(ref _projectorLayout, value);
            _ = _settings.SetAsync("projector_layout", value);
        }
    }

    public bool AutoStartListening
    {
        get => _autoStartListening;
        set
        {
            this.RaiseAndSetIfChanged(ref _autoStartListening, value);
            _ = _settings.SetBoolAsync("auto_start_listening", value);
        }
    }

    public List<string> FontSizeOptions { get; } = ["Small", "Medium", "Large", "Extra Large"];
    public List<string> BackgroundOptions { get; } = ["Black", "Dark Blue", "Dark Green", "Dark Purple", "Dark Red", "Charcoal"];
    public List<string> LayoutOptions { get; } = ["Full Screen", "Lower Third"];

    public int SelectedContentTab
    {
        get => _selectedContentTab;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedContentTab, value);
            if (value == SuggestionsTabIndex) HasNewSuggestions = false;
            if (value == TopicalTabIndex) HasNewTopical = false;
            if (value == SongsTabIndex) _ = SongSearch.RefreshAsync();
            this.RaisePropertyChanged(nameof(IsLibraryTab));
            this.RaisePropertyChanged(nameof(IsSuggestionsTab));
            this.RaisePropertyChanged(nameof(IsTopicalTab));
            this.RaisePropertyChanged(nameof(IsSongsTab));
            this.RaisePropertyChanged(nameof(ShowScriptureList));
            this.RaisePropertyChanged(nameof(ShowNowSinging));
        }
    }

    public bool IsLibraryTab => SelectedContentTab == 0;
    public bool IsSuggestionsTab => SelectedContentTab == SuggestionsTabIndex;
    public bool IsTopicalTab => SelectedContentTab == TopicalTabIndex;
    public bool IsSongsTab => SelectedContentTab == SongsTabIndex;

    // ───────────────────────── Top-level workspace mode (Bible vs Songs) ─────────────────────────
    // A service is either displaying scripture or singing. Each mode reconfigures the left sidebar
    // and the center tools. Themes is a dialog launcher, not a mode.
    private bool _isSongsMode;
    private bool _isMediaMode;

    public bool IsSongsMode
    {
        get => _isSongsMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSongsMode, value);
            this.RaisePropertyChanged(nameof(IsBibleMode));
            this.RaisePropertyChanged(nameof(LibraryTabLabel));
            this.RaisePropertyChanged(nameof(SearchWatermark));
            this.RaisePropertyChanged(nameof(ShowScriptureList));
            this.RaisePropertyChanged(nameof(ShowNowSinging));
        }
    }

    // Bible vs Songs is a sub-choice of the "content" workspace; Media is a third top-level workspace
    // that swaps the whole center area for the media bin + transport.
    public bool IsBibleMode => !IsSongsMode && !IsMediaMode;

    /// <summary>True when the center area shows the Media Playback view instead of the content tabs.</summary>
    public bool IsMediaMode
    {
        get => _isMediaMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isMediaMode, value);
            this.RaisePropertyChanged(nameof(IsBibleMode));
            this.RaisePropertyChanged(nameof(IsContentMode));
            this.RaisePropertyChanged(nameof(ShowScriptureList));
            this.RaisePropertyChanged(nameof(ShowNowSinging));
        }
    }

    /// <summary>True when the center area shows the normal content tabs (Bible or Songs).</summary>
    public bool IsContentMode => !IsMediaMode;

    public string SearchWatermark => IsSongsMode
        ? "Search your songs by title or lyric…"
        : "Search a scripture, e.g. John 3:16";

    /// <summary>Tab 0 is a shared content surface: scripture lookups in Bible mode, the loaded song's
    /// slides ("Now Singing") in Songs mode.</summary>
    public string LibraryTabLabel => IsSongsMode ? "Now Singing" : "Scripture";

    /// <summary>Tab 0 shows the scripture-lookup list only in Bible mode.</summary>
    public bool ShowScriptureList => IsLibraryTab && IsBibleMode;

    /// <summary>Tab 0 shows the loaded song's slide cards only in Songs mode.</summary>
    public bool ShowNowSinging => IsLibraryTab && IsSongsMode;

    public void EnterBibleMode()
    {
        IsMediaMode = false;
        IsSongsMode = false;
        // Scripture-driven service: keep the AI suggestions panel to scriptures only (no songs).
        _aiMatcher.IncludeContentMatches = false;
        if (SelectedContentTab == SongsTabIndex) SelectedContentTab = TopicalTabIndex;
        _ = ContentSearch.ResetForModeAsync(songsMode: false);
    }

    public void EnterSongsMode()
    {
        IsMediaMode = false;
        IsSongsMode = true;
        _aiMatcher.IncludeContentMatches = true;
        SelectedContentTab = SongsTabIndex;
        _ = ContentSearch.ResetForModeAsync(songsMode: true);
    }

    /// <summary>Switches the center area to the Media Playback view (graphics/videos + transport).</summary>
    public void EnterMediaMode() => IsMediaMode = true;

    public bool HasNewTopical
    {
        get => _hasNewTopical;
        set => this.RaiseAndSetIfChanged(ref _hasNewTopical, value);
    }

    public int AiSuggestionCount
    {
        get => _aiSuggestionCount;
        set => this.RaiseAndSetIfChanged(ref _aiSuggestionCount, value);
    }

    public bool HasNewSuggestions
    {
        get => _hasNewSuggestions;
        set => this.RaiseAndSetIfChanged(ref _hasNewSuggestions, value);
    }

    // ───────────────────────── Left sidebar: Library + Playlists ─────────────────────────

    /// <summary>Imported songs, shown in the sidebar Library section for fast referencing.</summary>
    public ObservableCollection<Song> LibrarySongs { get; } = [];

    private Song? _selectedLibrarySong;
    public Song? SelectedLibrarySong
    {
        get => _selectedLibrarySong;
        set => this.RaiseAndSetIfChanged(ref _selectedLibrarySong, value);
    }

    public bool HasLibrarySongs => LibrarySongs.Count > 0;

    /// <summary>Slides of the song currently opened in the "Now Singing" tab (Songs mode, tab 0).</summary>
    public ObservableCollection<SongSlideItem> NowSingingSlides { get; } = [];

    private string? _nowSingingTitle;
    public string? NowSingingTitle
    {
        get => _nowSingingTitle;
        private set => this.RaiseAndSetIfChanged(ref _nowSingingTitle, value);
    }

    public bool HasNowSinging => NowSingingSlides.Count > 0;

    // Operator-adjustable size of the Now Singing slide cards (1.0 = default). Each card scales its
    // own width, preview height and text off this so the operator can make slides bigger to read.
    private double _slideScale = 1.0;
    public double SlideScale
    {
        get => _slideScale;
        set
        {
            this.RaiseAndSetIfChanged(ref _slideScale, value);
            foreach (var slide in NowSingingSlides) slide.ApplyScale(value);
        }
    }

    // The Now Singing card the operator (or lyric-follow) has teed up. Bound to the list's selection
    // so pressing Enter / double-clicking sends exactly this slide live.
    private SongSlideItem? _selectedNowSingingSlide;
    public SongSlideItem? SelectedNowSingingSlide
    {
        get => _selectedNowSingingSlide;
        set => this.RaiseAndSetIfChanged(ref _selectedNowSingingSlide, value);
    }

    // ----- Lyric follow (AI assist) -----
    // Conservative, operator-in-command matching scoped strictly to the open song's slides. It only
    // ever suggests/teeing-up; it never changes songs and (in Assist) never sends live on its own.
    private const double FollowMinScore = 1.1;     // below this: hold, nothing is confident enough
    private const double FollowStrongScore = 1.8;  // required to jump to a non-adjacent slide
    private const double FollowNearMargin = 0.35;  // best must beat runner-up by this
    private const double FollowJumpMargin = 0.8;   // a far jump must beat the current slide by this
    private const int FollowDwell = 2;             // must win this many segments in a row before acting
    private static readonly TimeSpan FollowCooldown = TimeSpan.FromSeconds(1.2);

    private readonly List<string> _followWindow = [];
    private int _followPendingTarget = -1;
    private int _followPendingCount;
    private int _lastFollowIndex = -1;
    private DateTime _followCooldownUntil = DateTime.MinValue;

    private LyricFollowMode _followMode = LyricFollowMode.Off;
    public LyricFollowMode FollowMode
    {
        get => _followMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _followMode, value);
            this.RaisePropertyChanged(nameof(IsFollowOn));
            OnFollowModeChanged();
        }
    }

    public bool IsFollowOn => _followMode != LyricFollowMode.Off;

    private string _followStatus = "";
    public string FollowStatus
    {
        get => _followStatus;
        private set => this.RaiseAndSetIfChanged(ref _followStatus, value);
    }

    private bool _libraryExpanded = true;
    public bool LibraryExpanded
    {
        get => _libraryExpanded;
        set => this.RaiseAndSetIfChanged(ref _libraryExpanded, value);
    }

    private bool _playlistsExpanded = true;
    public bool PlaylistsExpanded
    {
        get => _playlistsExpanded;
        set => this.RaiseAndSetIfChanged(ref _playlistsExpanded, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleLibrarySectionCommand { get; }
    public ReactiveCommand<Unit, Unit> TogglePlaylistsSectionCommand { get; }

    /// <summary>Saved set lists shown in the left sidebar for fast referencing.</summary>
    public ObservableCollection<SavedPlaylist> Playlists { get; } = [];

    private SavedPlaylist? _selectedPlaylist;
    public SavedPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set => this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);
    }

    private string _newPlaylistName = string.Empty;
    public string NewPlaylistName
    {
        get => _newPlaylistName;
        set => this.RaiseAndSetIfChanged(ref _newPlaylistName, value);
    }

    private bool _isNamingPlaylist;
    /// <summary>Whether the inline "name your playlist" row is shown (toggled by the Playlists "+").</summary>
    public bool IsNamingPlaylist
    {
        get => _isNamingPlaylist;
        set => this.RaiseAndSetIfChanged(ref _isNamingPlaylist, value);
    }

    public bool HasPlaylists => Playlists.Count > 0;

    public ReactiveCommand<Unit, Unit> SavePlaylistCommand { get; }
    public ReactiveCommand<Unit, Unit> BeginNamePlaylistCommand { get; }
    public ReactiveCommand<SavedPlaylist, Unit> LoadPlaylistCommand { get; }
    public ReactiveCommand<SavedPlaylist, Unit> DeletePlaylistCommand { get; }

    public ReactiveCommand<Unit, Unit> BlankCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleFollowCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleScreenOutputCommand { get; }
    public ReactiveCommand<Unit, Unit> TransitionCommand { get; }
    public ReactiveCommand<Unit, Unit> AddToQueueCommand { get; }
    public ReactiveCommand<Unit, Unit> AddAllToQueueCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowLibraryTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSuggestionsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowTopicalTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSongsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowBibleModeCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSongsModeCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowMediaModeCommand { get; }
    public ReactiveCommand<Unit, Unit> SignOutCommand { get; }

    public OperatorViewModel(
        IProjectionService projectionService,
        IContentLibraryService contentLibrary,
        ISyncScheduler syncScheduler,
        SettingsRepository settings,
        ITranscriptionService transcriptionService,
        ISuggestionEngine suggestionEngine,
        IAiMatcherService aiMatcher,
        BibleCacheService bibleCache,
        IThemeService themes,
        IScriptureSearchService scriptureSearch,
        ISongSearchService songSearch,
        IProPresenterService proPresenter,
        ILiveBackgroundService liveBackground,
        IAnnouncementService announcements,
        ILayerService layers)
    {
        _projection = projectionService;
        _settings = settings;
        _aiMatcher = aiMatcher;
        _bibleCache = bibleCache;
        _themes = themes;
        _scriptureSearch = scriptureSearch;
        _transcriptionService = transcriptionService;
        _contentLibrary = contentLibrary;
        _proPresenter = proPresenter;
        _liveBackground = liveBackground;
        _announcements = announcements;
        _layers = layers;
        Backgrounds = new BackgroundsViewModel(liveBackground);
        MediaPlayback = new Operator.MediaPlaybackViewModel(announcements, Outputs);

        ContentSearch = new ContentSearchViewModel(contentLibrary);
        SongImport = new SongImportViewModel(contentLibrary);
        ServiceQueue = new ServiceQueueViewModel(projectionService, themes);
        Transcription = new TranscriptionViewModel(transcriptionService, suggestionEngine, projectionService);
        TopicalSearch = new TopicalSearchViewModel(scriptureSearch);
        SongSearch = new SongSearchViewModel(songSearch);
        ProPresenter = new ProPresenterViewModel(proPresenter);
        ProgramPreview = new ProjectorViewModel(_projection, themes, null, liveBackground, announcements, MediaTarget.AllScreens, layers);

        // Typing a lyric (from the top search bar in Songs mode) always brings up the Songs results tab.
        SongSearch.WhenAnyValue(x => x.Query)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(q =>
            {
                if (IsSongsMode && !string.IsNullOrWhiteSpace(q) && SelectedContentTab != SongsTabIndex)
                    SelectedContentTab = SongsTabIndex;
            });

        RefreshThemeOptions();
        _themes.Changed
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshThemeOptions());

        BlankCommand = ReactiveCommand.Create(() => { _projection.GoBlank(); _liveScriptureRef = null; ClearContentLiveHighlights(); foreach (var s in NowSingingSlides) { s.IsLive = false; s.IsSuggested = false; } });
        ToggleFollowCommand = ReactiveCommand.Create(() =>
        {
            FollowMode = FollowMode == LyricFollowMode.Off ? LyricFollowMode.Assist : LyricFollowMode.Off;
        });
        ToggleScreenOutputCommand = ReactiveCommand.Create(() => { ScreenOutputEnabled = !ScreenOutputEnabled; });

        SavePlaylistCommand = ReactiveCommand.Create(SaveCurrentAsPlaylist);
        BeginNamePlaylistCommand = ReactiveCommand.Create(() => { PlaylistsExpanded = true; IsNamingPlaylist = !IsNamingPlaylist; });
        LoadPlaylistCommand = ReactiveCommand.Create<SavedPlaylist>(LoadPlaylist);
        DeletePlaylistCommand = ReactiveCommand.Create<SavedPlaylist>(DeletePlaylist);
        ToggleLibrarySectionCommand = ReactiveCommand.Create(() => { LibraryExpanded = !LibraryExpanded; });
        TogglePlaylistsSectionCommand = ReactiveCommand.Create(() => { PlaylistsExpanded = !PlaylistsExpanded; });
        ShowLibraryTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = 0; });
        ShowSuggestionsTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = SuggestionsTabIndex; });
        ShowTopicalTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = TopicalTabIndex; });
        ShowSongsTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = SongsTabIndex; });
        ShowBibleModeCommand = ReactiveCommand.Create(EnterBibleMode);
        ShowSongsModeCommand = ReactiveCommand.Create(EnterSongsMode);
        ShowMediaModeCommand = ReactiveCommand.Create(EnterMediaMode);

        // Progress reporter created on the UI thread so index status marshals back correctly.
        _indexProgress = new Progress<string>(msg => TopicalSearch.StatusText = msg);

        // Listen for spoken "find me the scripture about ..." requests and surface matches in the tab.
        _transcriptionService.Segments
            .Subscribe(s => OnSpokenSegment(s.Text));

        // Lyric follow: score what's being sung against the open song's slides (UI thread, since it
        // mutates the slide highlights and the list selection).
        _transcriptionService.Segments
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnFollowSegment);

        // Reflect mic state in the follow status (e.g. "start the mic to begin" → "Listening…").
        Transcription.WhenAnyValue(x => x.IsListening)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => { if (FollowMode != LyricFollowMode.Off) OnFollowModeChanged(); });

        var canTransition = this.WhenAnyValue<OperatorViewModel, bool>(x => x.HasPreview);
        TransitionCommand = ReactiveCommand.Create(DoTransition, canTransition);

        var canAdd = ContentSearch.WhenAnyValue<ContentSearchViewModel, ContentItem?>(x => x.SelectedItem)
            .Select(i => i is not null);
        AddToQueueCommand = ReactiveCommand.Create(DoAddToQueue, canAdd);

        AddAllToQueueCommand = ReactiveCommand.Create(DoAddAllToQueue);

        SyncStatus = DescribeSync(syncScheduler.Status);
        syncScheduler.StatusChanged += info =>
            RxApp.MainThreadScheduler.Schedule(() => SyncStatus = DescribeSync(info));
        SignOutCommand = ReactiveCommand.Create(() => SignOutRequested?.Invoke());

        Transcription.Suggestions.CollectionChanged += (_, e) =>
        {
            AiSuggestionCount = Transcription.Suggestions.Count;
            if (e.Action == NotifyCollectionChangedAction.Add && SelectedContentTab != SuggestionsTabIndex)
                HasNewSuggestions = true;
        };

        SongImport.SongImported += OnSongImported;

        ContentSearch.WhenAnyValue<ContentSearchViewModel, ContentItem?>(x => x.SelectedItem)
            .Subscribe(item =>
            {
                if (item is not null)
                {
                    SetPreview(item.Title, item.Body, item.Footer);
                    if (SingleClickGoesLive) SendItemToLive(item);
                }
                else if (Transcription.SelectedSuggestion is null)
                {
                    ClearPreview();
                }
            });

        Transcription.WhenAnyValue<TranscriptionViewModel, SuggestionItem?>(x => x.SelectedSuggestion)
            .Subscribe(item =>
            {
                if (item is not null)
                    SetPreview(item.Title, item.Body, item.Footer);
                else if (ContentSearch.SelectedItem is null)
                    ClearPreview();
            });

        // Keep the Program thumbnail's aspect ratio in sync with whichever physical screen is active
        // (screens are added by the window layer after construction).
        Outputs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (OutputRow row in e.NewItems)
                    row.WhenAnyValue(r => r.IsActive)
                        .Subscribe(_ => UpdatePreviewAspect());
            UpdatePreviewAspect();
        };

        this.WhenActivated(disposables =>
        {
            disposables.Add(_feedSub);
            BindToFeed();
            UpdatePreviewAspect();
        });
    }

    /// <summary>
    /// Picks the resolution of an active physical screen so the Program thumbnail matches the real
    /// screen shape. Falls back to any display, then 16:9.
    /// </summary>
    private void UpdatePreviewAspect()
    {
        bool HasGeometry(OutputRow o) =>
            o.Kind is OutputKind.Display or OutputKind.Windowed && o.Display is { Width: > 0, Height: > 0 };

        var chosen =
            Outputs.FirstOrDefault(o => HasGeometry(o) && o.IsActive)
            ?? Outputs.FirstOrDefault(HasGeometry);

        var display = chosen?.Display
            ?? AvailableDisplays.FirstOrDefault(d => !d.IsWindowedPreview && d.Width > 0 && d.Height > 0)
            ?? AvailableDisplays.FirstOrDefault(d => d.Width > 0 && d.Height > 0);

        PreviewWidth = display is { Width: > 0 } ? display.Width : 1920;
        PreviewHeight = display is { Height: > 0 } ? display.Height : 1080;
    }

    // Keeps the shared per-output theme picker in sync with the theme library. Preserves each
    // output's current selection across rebuilds (the list instance is shared with every OutputRow).
    private void RefreshThemeOptions()
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(OutputRow.FollowContent);
        foreach (var t in _themes.Themes)
            ThemeOptions.Add(t.Name);

        LiveThemeOptions.Clear();
        foreach (var t in _themes.Themes)
            LiveThemeOptions.Add(t.Name);

        // The active assignment may have changed (e.g. via Theme Studio); refresh the picker's value.
        this.RaisePropertyChanged(nameof(LiveThemeName));
    }

    private void BindToFeed()
    {
        var sub = new CompositeDisposable();

        _projection.CurrentSlide
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(slide =>
            {
                LiveTitle = slide.Title;
                LiveBody = slide.Body;
                LiveFooter = slide.Footer;
                IsLive = slide.Type != SlideType.Blank;
            })
            .DisposeWith(sub);

        _projection.Position
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(pos =>
            {
                HasMultipleSlides = pos.IsMulti;
                DeckPositionText = pos.IsMulti ? pos.Label : string.Empty;
            })
            .DisposeWith(sub);

        _feedSub.Disposable = sub;
    }

    private void OnSongImported()
    {
        _ = ReloadLibraryAsync();
    }

    private async Task ReloadLibraryAsync()
    {
        try
        {
            await ContentSearch.LoadAllContentAsync();
            await LoadLibrarySongsAsync();
            PopulateMatcherIndex();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reload content library after song import");
        }
    }

    /// <summary>Loads the distinct imported songs for the sidebar Library section.</summary>
    private async Task LoadLibrarySongsAsync()
    {
        try
        {
            var songs = await _contentLibrary.GetAllSongsAsync();
            LibrarySongs.Clear();
            foreach (var song in songs.OrderBy(s => s.Title))
                LibrarySongs.Add(song);
            this.RaisePropertyChanged(nameof(HasLibrarySongs));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load sidebar library songs");
        }
    }

    private void SetPreview(string title, string body, string footer)
    {
        PreviewTitle = title;
        PreviewBody = body;
        PreviewFooter = footer;
        HasPreview = true;
    }

    private void ClearPreview()
    {
        PreviewTitle = string.Empty;
        PreviewBody = string.Empty;
        PreviewFooter = string.Empty;
        HasPreview = false;
    }

    public void SendItemToLive(ContentItem? item)
    {
        if (item is null) return;
        var type = item.Type.ToSlideType();
        var theme = _themes.ResolveFor(type);
        _projection.ProjectDeck(DeckBuilder.Build(type, item.Title, item.Body, item.Footer, theme, item.LinesPerSlide));
        _liveScriptureRef = ReferenceFor(item);
        SetLiveSlideType(type);
        // Anything else going live invalidates a Now Singing slide highlight + suggestion.
        foreach (var slide in NowSingingSlides) { slide.IsLive = false; slide.IsSuggested = false; }
        // Clear previous content/suggestion live highlights, then mark the new item.
        ClearContentLiveHighlights();
        _liveContentItem = item;
        item.IsLive = true;
        // A manual/operator live action takes precedence over lyric-follow: pause it briefly.
        _followCooldownUntil = DateTime.UtcNow + FollowCooldown;
        _followPendingTarget = -1;
        _followPendingCount = 0;
        StatusText = $"Live: {item.Title}";
    }

    /// <summary>Projects a single Now Singing slide and marks it as the one currently on output.</summary>
    public void SendSlideLive(SongSlideItem? slide)
    {
        if (slide is null) return;
        SendItemToLive(slide.Item);
        slide.IsLive = true;
        _lastFollowIndex = NowSingingSlides.IndexOf(slide);
    }

    public void SendSuggestionToLive(SuggestionItem? item)
    {
        if (item is null) return;
        var theme = _themes.ResolveFor(SlideType.Scripture);
        _projection.ProjectDeck(DeckBuilder.Build(SlideType.Scripture, item.Title, item.Body, item.Footer, theme));
        _liveScriptureRef = item.IsScripture ? ScriptureReferenceParser.TryParse(item.Title) : null;
        SetLiveSlideType(SlideType.Scripture);
        foreach (var slide in NowSingingSlides) { slide.IsLive = false; slide.IsSuggested = false; }
        ClearContentLiveHighlights();
        _liveSuggestionItem = item;
        item.IsLive = true;
        StatusText = $"Live: {item.Title}";
    }

    /// <summary>Tracks the live slide's type and refreshes the program-preview theme picker so it
    /// targets (and displays) that type's current theme assignment.</summary>
    private void SetLiveSlideType(SlideType type)
    {
        _liveSlideType = type;
        this.RaisePropertyChanged(nameof(LiveThemeName));
    }

    /// <summary>Clears the IsLive ring from the previously-live content item and suggestion.</summary>
    private void ClearContentLiveHighlights()
    {
        if (_liveContentItem is not null) { _liveContentItem.IsLive = false; _liveContentItem = null; }
        if (_liveSuggestionItem is not null) { _liveSuggestionItem.IsLive = false; _liveSuggestionItem = null; }
    }

    /// <summary>Pins a scripture from the search list to the Bookmarks sidebar so the operator can
    /// jump straight back to it (e.g. when the preacher says "take me back to that verse").</summary>
    public void BookmarkScripture(ContentItem? item)
    {
        if (item is null || !item.IsScripture) return;

        var reference = ReferenceFor(item);
        var contentId = reference is not null
            ? $"scripture:{reference.Book}:{reference.Chapter}:{reference.VerseStart}"
            : $"scripture:{item.Title}";

        Transcription.AddBookmark(new SuggestionItem
        {
            ContentId = contentId,
            Title = item.Title,
            Body = item.Body,
            Footer = string.IsNullOrWhiteSpace(item.Footer) ? item.Title : item.Footer,
            MatchType = "scripture_reference",
            IsBookmarked = true,
        });
        StatusText = $"Bookmarked {item.Title}";
    }

    private static ScriptureReference? ReferenceFor(ContentItem item)
    {
        if (!item.IsScripture) return null;
        if (item.Source is ScripturePassage p)
            return new ScriptureReference(p.Book, p.Chapter, p.VerseStart, p.VerseEnd);
        return ScriptureReferenceParser.TryParse(item.Title);
    }

    /// <summary>
    /// Re-renders whatever scripture is currently live into the newly selected translation, so
    /// switching translations updates the verse already on screen — not just the next one.
    /// </summary>
    private async Task RefreshLiveTranslationAsync(string translation)
    {
        var reference = _liveScriptureRef;
        if (reference is null) return;

        try
        {
            var verses = await _contentLibrary
                .GetOrFetchVersesAsync(reference, translation, localOnly: false)
                .ConfigureAwait(true);
            if (verses.Count == 0) return;

            var ordered = verses.OrderBy(v => v.VerseStart).ToList();
            var first = ordered[0];
            var last = ordered[^1];

            var label = ordered.Count == 1
                ? first.Reference
                : $"{first.Book} {first.Chapter}:{first.VerseStart}-{last.VerseStart}";
            var body = ordered.Count == 1
                ? first.Text
                : string.Join(" ", ordered.Select(v => v.Text));
            var footer = $"{label} ({translation})";

            var theme = _themes.ResolveFor(SlideType.Scripture);
            _projection.ProjectDeck(DeckBuilder.Build(SlideType.Scripture, label, body, footer, theme));
            StatusText = $"Switched live to {translation}: {label}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh live scripture into {Translation}", translation);
        }
    }

    /// <summary>
    /// Pulls the whole chapter behind a scripture suggestion into the Library tab so the operator
    /// can browse and project verses manually without waiting on the live AI.
    /// </summary>
    public void ShowFullChapter(SuggestionItem? item)
    {
        if (item is null) return;
        if (TryParseScriptureId(item.ContentId, out var book, out var chapter))
            LoadChapterIntoLibrary(book, chapter);
    }

    public void ShowFullChapter(ContentItem? item)
    {
        if (item is null) return;

        if (item.Source is ScripturePassage passage)
        {
            LoadChapterIntoLibrary(passage.Book, passage.Chapter);
        }
        else if (TryParseReferenceText(item.Title, out var book, out var chapter))
        {
            LoadChapterIntoLibrary(book, chapter);
        }
    }

    private void LoadChapterIntoLibrary(string book, int chapter)
    {
        SelectedContentTab = 0;
        StatusText = $"Loading {book} {chapter}...";
        _ = ContentSearch.LoadFullChapterAsync(book, chapter);
    }

    // Detects a spoken "find the scripture about ..." request and populates the Find Scripture tab.
    private void OnSpokenSegment(string text)
    {
        var topic = TopicalRequestParser.ExtractTopic(text);
        if (string.IsNullOrWhiteSpace(topic)) return;

        if (SelectedContentTab != TopicalTabIndex)
            HasNewTopical = true;

        _ = TopicalSearch.RunAutoSearchAsync(topic);
    }

    // ContentId shape: "scripture:{Book}:{Chapter}:{VerseStart}"; Book may contain spaces ("1 John").
    private static bool TryParseScriptureId(string contentId, out string book, out int chapter)
    {
        book = string.Empty;
        chapter = 0;
        if (string.IsNullOrEmpty(contentId)) return false;

        var parts = contentId.Split(':');
        if (parts.Length < 4 || !parts[0].Equals("scripture", StringComparison.OrdinalIgnoreCase))
            return false;

        book = parts[1];
        return int.TryParse(parts[2], out chapter) && chapter > 0 && book.Length > 0;
    }

    private static bool TryParseReferenceText(string reference, out string book, out int chapter)
    {
        var parsed = ScriptureReferenceParser.TryParse(reference);
        if (parsed is not null)
        {
            book = parsed.Book;
            chapter = parsed.Chapter;
            return true;
        }
        book = string.Empty;
        chapter = 0;
        return false;
    }

    /// <summary>
    /// Arrow-right behaviour: page forward through the live deck; when on the last page,
    /// roll over to the next queue item.
    /// </summary>
    public void AdvanceForward()
    {
        if (_projection.MoveNext())
            return;
        if (ServiceQueue.CanGoNext)
            ServiceQueue.NextCommand.Execute().Subscribe();
    }

    /// <summary>
    /// Arrow-left behaviour: page back through the live deck; when on the first page,
    /// roll over to the previous queue item.
    /// </summary>
    public void AdvanceBackward()
    {
        if (_projection.MovePrev())
            return;
        if (ServiceQueue.CanGoPrev)
            ServiceQueue.PrevCommand.Execute().Subscribe();
    }

    private void DoTransition()
    {
        SendItemToLive(ContentSearch.SelectedItem);
    }

    private void DoAddToQueue()
    {
        if (ContentSearch.SelectedItem is not null)
        {
            ServiceQueue.AddItem(ContentSearch.SelectedItem);
            StatusText = $"Queued: {ContentSearch.SelectedItem.Title}";
        }
    }

    private void DoAddAllToQueue()
    {
        ServiceQueue.AddAllItems(ContentSearch.Results);
        StatusText = $"Queued {ContentSearch.Results.Count} items";
    }

    private const string PlaylistsKey = "playlists";

    /// <summary>Saves the current service queue as a named, reusable playlist.</summary>
    private void SaveCurrentAsPlaylist()
    {
        var items = ServiceQueue.Snapshot();
        if (items.Count == 0)
        {
            StatusText = "Queue is empty — add items before saving a playlist.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewPlaylistName)
            ? $"Set {Playlists.Count + 1}"
            : NewPlaylistName.Trim();

        // Overwrite a same-named playlist rather than creating a duplicate.
        var existing = Playlists.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Playlists.Remove(existing);

        Playlists.Insert(0, new SavedPlaylist { Name = name, Items = items });
        NewPlaylistName = string.Empty;
        IsNamingPlaylist = false;
        this.RaisePropertyChanged(nameof(HasPlaylists));
        StatusText = $"Saved playlist: {name} ({items.Count} items)";
        _ = PersistPlaylistsAsync();
    }

    /// <summary>Opens a song from the sidebar Library into the "Now Singing" tab as projectable slide
    /// cards. Double-clicking a slide there is what actually sends a section live.</summary>
    public void OpenSong(Song? song)
    {
        if (song is null) return;
        SelectedLibrarySong = song;
        RebuildNowSingingSlides(song);
        NowSingingTitle = string.IsNullOrWhiteSpace(song.Artist) ? song.Title : $"{song.Title}  ·  {song.Artist}";
        _nowSingingLines = song.LinesPerSlide;
        this.RaisePropertyChanged(nameof(NowSingingLinesPerSlide));
        this.RaisePropertyChanged(nameof(HasNowSinging));
        SelectedContentTab = 0;
        StatusText = $"Opened \"{song.Title}\" — double-click a slide to project it";
    }

    /// <summary>Rebuilds the Now Singing cards so each card equals one projected slide: sections are
    /// paginated exactly as they'll appear on screen (honouring the song's lines-per-slide breaking).</summary>
    private void RebuildNowSingingSlides(Song song)
    {
        ResetFollowState();
        NowSingingSlides.Clear();
        var theme = _themes.ResolveFor(SlideType.Lyric);
        foreach (var section in song.Sections.OrderBy(s => s.SectionOrder))
        {
            var pages = DeckBuilder.Build(SlideType.Lyric, "", section.Text, "", theme, song.LinesPerSlide).Slides;
            var multi = pages.Count > 1;
            for (var i = 0; i < pages.Count; i++)
            {
                var label = multi ? $"{section.Label} ({i + 1})" : section.Label;
                var slide = new SongSlideItem(song, section, pages[i].Body, label);
                slide.ApplyScale(_slideScale);
                NowSingingSlides.Add(slide);
            }
        }
    }

    // ----- Lyric follow (AI assist) -----

    private void OnFollowModeChanged()
    {
        ResetFollowState();
        if (FollowMode == LyricFollowMode.Off)
        {
            FollowStatus = "";
            return;
        }
        FollowStatus = !HasNowSinging
            ? "Follow on — open a song"
            : !Transcription.IsListening
                ? "Follow on — start the mic to begin"
                : "Listening…";
    }

    /// <summary>Clears the rolling transcript window, any pending decision and slide suggestions.</summary>
    private void ResetFollowState()
    {
        _followWindow.Clear();
        _followPendingTarget = -1;
        _followPendingCount = 0;
        _lastFollowIndex = -1;
        _followCooldownUntil = DateTime.MinValue;
        foreach (var slide in NowSingingSlides) slide.IsSuggested = false;
    }

    /// <summary>
    /// Core of lyric-follow: append the heard words to a rolling window, score them against the open
    /// song's slides, and (in Assist) tee up the winner once it has been stable long enough. Heavily
    /// guarded so it never twitches: confidence floor, ambiguity margin, stickiness to the current
    /// slide, a stronger bar for non-adjacent jumps, a dwell requirement and a short cooldown.
    /// </summary>
    private void OnFollowSegment(TranscriptionSegment segment)
    {
        if (FollowMode == LyricFollowMode.Off || NowSingingSlides.Count == 0) return;

        foreach (var token in LyricFollow.Tokenize(segment.Text))
            _followWindow.Add(token);
        // Keep only the most recent words — sung phrases are short and stale words cause false jumps.
        const int windowSize = 12;
        if (_followWindow.Count > windowSize)
            _followWindow.RemoveRange(0, _followWindow.Count - windowSize);

        if (_followWindow.Count < 2) return;
        if (DateTime.UtcNow < _followCooldownUntil) return;

        var slides = NowSingingSlides.Select(s => s.MatchTokens).ToList();
        var eval = LyricFollow.Evaluate(slides, _followWindow);
        if (eval.BestIndex < 0 || eval.BestScore < FollowMinScore) return; // hold: nothing confident

        var current = CurrentFollowIndex();
        var target = eval.BestIndex;
        var margin = eval.BestScore - eval.SecondScore;

        if (target != current)
        {
            var adjacent = current >= 0 && target == current + 1;
            if (adjacent)
            {
                if (margin < FollowNearMargin) return;
            }
            else
            {
                // A jump backward/forward to a non-neighbour (e.g. leader returns to the chorus) must
                // clear a higher bar so ordinary noise can't drag us across the song.
                var currentScore = current >= 0 && current < eval.Scores.Count ? eval.Scores[current] : 0;
                if (eval.BestScore < FollowStrongScore) return;
                if (eval.BestScore - currentScore < FollowJumpMargin) return;
                if (margin < FollowNearMargin) return;
            }
        }

        // Dwell: the same target must win several segments in a row before we commit.
        if (_followPendingTarget == target)
        {
            _followPendingCount++;
        }
        else
        {
            _followPendingTarget = target;
            _followPendingCount = 1;
        }
        if (_followPendingCount < FollowDwell) return;

        CommitFollowSuggestion(target, eval.BestScore);
    }

    /// <summary>The slide follow treats as "where we are": the live one, else the last suggestion.</summary>
    private int CurrentFollowIndex()
    {
        for (var i = 0; i < NowSingingSlides.Count; i++)
            if (NowSingingSlides[i].IsLive) return i;
        return _lastFollowIndex;
    }

    /// <summary>Assist behaviour: highlight + tee up the slide, but never send it live automatically.</summary>
    private void CommitFollowSuggestion(int index, double score)
    {
        if (index < 0 || index >= NowSingingSlides.Count) return;
        var target = NowSingingSlides[index];

        for (var i = 0; i < NowSingingSlides.Count; i++)
            NowSingingSlides[i].IsSuggested = i == index && !NowSingingSlides[i].IsLive;

        _lastFollowIndex = index;
        SelectedNowSingingSlide = target;          // tees the card up for a deliberate send
        SetPreview(target.Item.Title, target.Item.Body, target.Item.Footer);

        var confidence = (int)Math.Round(100 * Math.Clamp(score / FollowStrongScore, 0, 1));
        FollowStatus = $"Following: {target.Label} · {confidence}%";
    }

    /// <summary>Lines-per-slide options offered in the editor and the Now Singing quick selector
    /// (0 = auto-fit to the theme).</summary>
    public IReadOnlyList<int> LinesPerSlideOptions { get; } = [0, 1, 2, 3, 4, 5, 6, 8];

    private int _nowSingingLines;
    /// <summary>How many lyric lines each slide breaks into for the opened song. Changing it re-pages
    /// the cards live and persists the choice on the song.</summary>
    public int NowSingingLinesPerSlide
    {
        get => _nowSingingLines;
        set
        {
            if (_nowSingingLines == value) return;
            this.RaiseAndSetIfChanged(ref _nowSingingLines, value);
            ApplyNowSingingLines(value);
        }
    }

    private void ApplyNowSingingLines(int lines)
    {
        var song = SelectedLibrarySong;
        if (song is null) return;
        song.LinesPerSlide = lines;
        RebuildNowSingingSlides(song);
        this.RaisePropertyChanged(nameof(HasNowSinging));
        _ = SaveSongQuietAsync(song);
        StatusText = lines == 0
            ? "Slides: auto-fit to theme"
            : $"Slides: {lines} line{(lines == 1 ? "" : "s")} each";
    }

    private async Task SaveSongQuietAsync(Song song)
    {
        try { await _contentLibrary.SaveSongAsync(song); }
        catch (Exception ex) { Log.Warning(ex, "Failed to persist lines-per-slide for {Title}", song.Title); }
    }

    /// <summary>Steps the live output to the next (+1) or previous (-1) Now Singing slide.</summary>
    public void StepLive(int direction)
    {
        if (NowSingingSlides.Count == 0) return;
        var idx = -1;
        for (var i = 0; i < NowSingingSlides.Count; i++)
            if (NowSingingSlides[i].IsLive) { idx = i; break; }

        var next = idx < 0
            ? (direction > 0 ? 0 : NowSingingSlides.Count - 1)
            : Math.Clamp(idx + direction, 0, NowSingingSlides.Count - 1);
        SendSlideLive(NowSingingSlides[next]);
    }

    /// <summary>Projects a self-ticking countdown to the active channel (e.g. a pre-service timer).</summary>
    public void StartCountdown(double minutes, string heading, string doneMessage)
    {
        var target = DateTime.UtcNow.AddMinutes(Math.Max(0, minutes));
        _projection.ProjectSlide(Slide.Countdown(target, heading?.Trim() ?? string.Empty, doneMessage?.Trim() ?? string.Empty));
        SetLiveSlideType(SlideType.Countdown);
        StatusText = $"Countdown started — {minutes:0.#} min";
    }

    /// <summary>Projects a self-ticking wall clock to the active channel.</summary>
    public void ShowClock(string heading, string format = "h:mm tt")
    {
        _projection.ProjectSlide(Slide.Clock(heading?.Trim() ?? string.Empty, format));
        SetLiveSlideType(SlideType.Clock);
        StatusText = "Clock on screen";
    }

    /// <summary>Persists edits made to a song (e.g. from the slide quick-edit dialog), refreshes the
    /// library, and reloads the "Now Singing" slides so the change appears immediately.</summary>
    public async Task SaveSongEditAsync(Song song)
    {
        try
        {
            var saved = await _contentLibrary.SaveSongAsync(song);
            await RefreshLibraryAsync();
            OpenSong(saved);
            StatusText = $"Saved \"{saved.Title}\"";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save song edit for {Title}", song.Title);
        }
    }

    /// <summary>Loads every section of a song into the Service Queue, ready to project.</summary>
    public void LoadSongToQueue(Song? song)
    {
        if (song is null) return;
        var items = SongToItems(song).ToList();
        if (items.Count == 0) return;
        ServiceQueue.AddAllItems(items);
        StatusText = $"Loaded \"{song.Title}\" — {items.Count} slide{(items.Count == 1 ? "" : "s")} queued";
    }

    /// <summary>Projects a song's first slide to Live immediately (double-click action).</summary>
    public void StartSongLive(Song? song)
    {
        if (song is null) return;
        var first = SongToItems(song).FirstOrDefault();
        if (first is null) return;
        SendItemToLive(first);
        StatusText = $"Live: \"{song.Title}\" — {first.Tag}";
    }

    private static IEnumerable<ContentItem> SongToItems(Song song) =>
        song.Sections.Select(s => SectionToItem(song, s));

    private static ContentItem SectionToItem(Song song, SongSection section) => new()
    {
        Type = ContentItemType.Song,
        Title = $"{song.Title} — {section.Label}",
        Subtitle = song.Artist ?? "",
        Body = section.Text,
        Tag = section.Label,
        Footer = song.Title,
        LinesPerSlide = song.LinesPerSlide,
        Source = song,
    };

    /// <summary>Replaces the service queue with a saved playlist's items.</summary>
    public void LoadPlaylist(SavedPlaylist? playlist)
    {
        if (playlist is null) return;
        ServiceQueue.LoadSlides(playlist.Items.Select(CopySlide));
        StatusText = $"Loaded playlist: {playlist.Name}";
    }

    public void DeletePlaylist(SavedPlaylist? playlist)
    {
        if (playlist is null) return;
        Playlists.Remove(playlist);
        this.RaisePropertyChanged(nameof(HasPlaylists));
        _ = PersistPlaylistsAsync();
    }

    private static QueueSlide CopySlide(QueueSlide s) => new()
    {
        Title = s.Title,
        Body = s.Body,
        Footer = s.Footer,
        Tag = s.Tag,
        Icon = s.Icon,
        SlideType = s.SlideType,
    };

    private async Task PersistPlaylistsAsync()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Playlists.ToList());
            await _settings.SetAsync(PlaylistsKey, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist playlists");
        }
    }

    private async Task LoadPlaylistsAsync()
    {
        try
        {
            var json = await _settings.GetAsync(PlaylistsKey);
            if (string.IsNullOrWhiteSpace(json)) return;

            var saved = System.Text.Json.JsonSerializer.Deserialize<List<SavedPlaylist>>(json);
            if (saved is null) return;

            Playlists.Clear();
            foreach (var p in saved)
                Playlists.Add(p);
            this.RaisePropertyChanged(nameof(HasPlaylists));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load playlists");
        }
    }

    public async Task InitializeAsync()
    {
        _singleClickGoesLive = await _settings.GetBoolAsync("single_click_goes_live", false);
        this.RaisePropertyChanged(nameof(SingleClickGoesLive));

        _projectorFontSize = await _settings.GetAsync("projector_font_size") ?? "Large";
        this.RaisePropertyChanged(nameof(ProjectorFontSize));

        _projectorBackground = await _settings.GetAsync("projector_background") ?? "Black";
        this.RaisePropertyChanged(nameof(ProjectorBackground));

        _projectorLayout = await _settings.GetAsync("projector_layout") ?? "Full Screen";
        this.RaisePropertyChanged(nameof(ProjectorLayout));

        _autoStartListening = await _settings.GetBoolAsync("auto_start_listening", true);
        this.RaisePropertyChanged(nameof(AutoStartListening));

        await LoadPlaylistsAsync();

        try
        {
            var available = await _bibleCache.LoadAvailableTranslationsAsync();
            if (available.Count > 0)
            {
                ContentSearch.AvailableTranslations.Clear();
                foreach (var (id, _) in available)
                    ContentSearch.AvailableTranslations.Add(id);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load translation list from API");
        }

        var savedTranslation = await _settings.GetAsync("bible_translation");
        if (!string.IsNullOrEmpty(savedTranslation) && ContentSearch.AvailableTranslations.Contains(savedTranslation))
            ContentSearch.SelectedTranslation = savedTranslation;

        _aiMatcher.CurrentTranslation = ContentSearch.SelectedTranslation;
        // Match the AI suggestion scope to the current workspace mode (Bible = scripture only).
        _aiMatcher.IncludeContentMatches = IsSongsMode;
        TopicalSearch.Translation = ContentSearch.SelectedTranslation;

        ContentSearch.WhenAnyValue<ContentSearchViewModel, string>(x => x.SelectedTranslation)
            .Subscribe(t =>
            {
                _aiMatcher.CurrentTranslation = t;
                TopicalSearch.Translation = t;
                _ = _settings.SetAsync("bible_translation", t);
                _ = CacheTranslationAsync(t);
                _ = RefreshLiveTranslationAsync(t);
            });

        _bibleCache.StatusMessage
            .ObserveOn(RxApp.MainThreadScheduler)
            .Where(s => !string.IsNullOrEmpty(s))
            .Subscribe(s => StatusText = s);

        // Pre-warm the local Bible cache so live scripture lookups hit SQLite instead of the network.
        var cacheTask = CacheTranslationAsync(ContentSearch.SelectedTranslation);

        await ContentSearch.LoadAllContentAsync();
        await LoadLibrarySongsAsync();
        PopulateMatcherIndex();
        // Align the shared content tab with the starting mode (Bible = clean scripture tab).
        await ContentSearch.ResetForModeAsync(IsSongsMode);
        await Transcription.InitializeAsync();

        if (_autoStartListening)
        {
            // Give the cache a brief head start; uncached refs still hydrate on the fly.
            await Task.WhenAny(cacheTask, Task.Delay(PrewarmCacheWait));
            Transcription.ToggleListeningCommand.Execute().Subscribe();
        }
    }

    private async Task CacheTranslationAsync(string translation)
    {
        try
        {
            await _bibleCache.EnsureTranslationCachedAsync(translation);

            // Build (or load) the topical search index for this translation in the background so the
            // "Find Scripture" tab can do semantic lookups. Keyword search works regardless.
            _ = Task.Run(() => _scriptureSearch.EnsureIndexedAsync(translation, _indexProgress));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Bible cache download failed for {T}", translation);
        }
    }

    private void PopulateMatcherIndex()
    {
        var texts = ContentSearch.Results.Select(r => $"{r.Title}\n{r.Body}").ToList();
        var ids = ContentSearch.Results.Select(r => $"{r.Type}:{r.Title}").ToList();
        _aiMatcher.UpdateContentLibrary(texts, ids);
    }
}
