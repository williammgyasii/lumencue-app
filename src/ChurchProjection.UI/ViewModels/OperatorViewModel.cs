using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Threading;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Projection;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Models.Theme;
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
    private const int ParaphrasesTabIndex = 4;
    private const int NotesTabIndex = 5;
    private static readonly TimeSpan PrewarmCacheWait = TimeSpan.FromSeconds(2);

    private readonly IProjectionService _projection;
    private readonly SettingsRepository _settings;
    private readonly SerialDisposable _feedSub = new();
    private readonly IAiMatcherService _aiMatcher;
    private readonly BibleCacheService _bibleCache;
    private readonly IThemeService _themes;
    private readonly IScriptureSearchService _scriptureSearch;
    private readonly IScriptureParaphraseWatcher _paraphraseWatcher;
    private CancellationTokenSource? _paraphraseCts;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IContentLibraryService _contentLibrary;
    private readonly IProPresenterService _proPresenter;
    private readonly ILiveBackgroundService _liveBackground;
    private readonly IAnnouncementService _announcements;
    private readonly ILayerService _layers;
    private readonly IEntitlementService _entitlements;
    private readonly IThemeAssetStore _themeAssetStore;
    private readonly Progress<string> _indexProgress;

    // The scripture reference currently shown live, so a translation switch can re-render it.
    private ScriptureReference? _liveScriptureRef;
    private int _translationRefreshGeneration;
    private int _compareGeneration;
    private readonly List<string> _compareChosen = [];

    // The chapter we've already auto-loaded into the Scripture tab, so projecting another verse from
    // the same chapter doesn't reload it. Lets the operator instantly hop to adjacent verses (the
    // preacher's "give me 35 … 34 … 37" flow) without re-fetching.
    private (string Book, int Chapter)? _preloadedChapter;

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
    private bool _hasNewParaphrases;
    private string _projectorFontSize = "Large";
    private string _projectorBackground = "Black";
    private string _projectorLayout = "Full Screen";
    private bool _autoStartListening;
    private bool _isUpgradePromptOpen;
    private string _upgradePromptTitle = "";
    private string _upgradePromptMessage = "";
    private bool _isWhatsNewOpen;
    private bool _screenOutputEnabled = true;
    private double _previewWidth = 1920;
    private double _previewHeight = 1080;

    private readonly IUpdateService? _updates;
    private IDisposable? _updateMessageTimer;
    private IDisposable? _toastTimer;
    private string? _toastMessage;
    private bool _updateAvailable;
    private string _updateVersion = string.Empty;
    private string? _updateMessage;
    private bool _isCheckingForUpdate;
    private bool _isDownloadingUpdate;
    private bool _isInstallingUpdate;
    private int _downloadProgress;

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
    public TranscriptionViewModel Transcription { get; }
    public TopicalSearchViewModel TopicalSearch { get; }
    public SongSearchViewModel SongSearch { get; }
    public NotesViewModel Notes { get; }
    public ProPresenterViewModel ProPresenter { get; }

    /// <summary>The swappable background media palette (still images + motion loops).</summary>
    public BackgroundsViewModel Backgrounds { get; }

    /// <summary>Up to two other translations of the live verse, shown in Now Live.</summary>
    public ObservableCollection<LiveCompareCard> CompareCards { get; } = [];

    /// <summary>Checkbox rows in the Now Live cog. At most two can be selected.</summary>
    public ObservableCollection<LiveCompareOption> CompareOptions { get; } = [];

    public bool HasCompareCards => CompareCards.Count > 0;

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

    /// <summary>True while a newer version is available; drives the persistent blinking update toast.</summary>
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set => this.RaiseAndSetIfChanged(ref _updateAvailable, value);
    }

    /// <summary>The version offered by the pending update (e.g. "0.6.6").</summary>
    public string UpdateVersion
    {
        get => _updateVersion;
        private set => this.RaiseAndSetIfChanged(ref _updateVersion, value);
    }

    /// <summary>Transient feedback for a manual check (e.g. "You're on the latest version"); auto-clears.</summary>
    public string? UpdateMessage
    {
        get => _updateMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _updateMessage, value);
            this.RaisePropertyChanged(nameof(HasUpdateMessage));
        }
    }

    public bool HasUpdateMessage => !string.IsNullOrEmpty(_updateMessage);

    /// <summary>Transient warning toast (e.g. a referenced verse/chapter that isn't in the Bible);
    /// auto-clears after a few seconds.</summary>
    public string? ToastMessage
    {
        get => _toastMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _toastMessage, value);
            this.RaisePropertyChanged(nameof(HasToast));
        }
    }

    public bool HasToast => !string.IsNullOrEmpty(_toastMessage);

    // Shows a transient warning toast. Re-showing resets the dismiss timer so a burst keeps the
    // latest message visible for its full duration rather than vanishing early.
    private void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ToastMessage = message;
        _toastTimer?.Dispose();
        _toastTimer = Observable.Timer(TimeSpan.FromSeconds(4))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => ToastMessage = null);
    }

    /// <summary>True while a check is in flight (About page spinner / disabled buttons).</summary>
    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        private set { this.RaiseAndSetIfChanged(ref _isCheckingForUpdate, value); this.RaisePropertyChanged(nameof(IsUpdateBusy)); }
    }

    /// <summary>True while the update package is downloading; drives the progress bar.</summary>
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set { this.RaiseAndSetIfChanged(ref _isDownloadingUpdate, value); this.RaisePropertyChanged(nameof(IsUpdateBusy)); }
    }

    /// <summary>True once the download finishes and the app is about to restart.</summary>
    public bool IsInstallingUpdate
    {
        get => _isInstallingUpdate;
        private set { this.RaiseAndSetIfChanged(ref _isInstallingUpdate, value); this.RaisePropertyChanged(nameof(IsUpdateBusy)); }
    }

    /// <summary>Download progress (0–100) for the in-app download manager.</summary>
    public int DownloadProgress
    {
        get => _downloadProgress;
        private set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }

    /// <summary>True when any update operation is running (used to disable buttons).</summary>
    public bool IsUpdateBusy => _isCheckingForUpdate || _isDownloadingUpdate || _isInstallingUpdate;

    /// <summary>The running app version, shown in the status bar and About page; clickable to check for updates.</summary>
    public string AppVersion { get; }

    /// <summary>OS description for the About page (e.g. "Microsoft Windows 10.0.26200").</summary>
    public string OsDescription { get; }

    /// <summary>.NET runtime description for the About page.</summary>
    public string RuntimeDescription { get; }

    /// <summary>Downloads (with progress) and applies the pending update, then restarts.</summary>
    public ReactiveCommand<Unit, Unit> InstallUpdateCommand { get; }

    /// <summary>Manually checks for updates (shows transient feedback when nothing's found).</summary>
    public ReactiveCommand<Unit, Unit> CheckForUpdatesCommand { get; }

    /// <summary>Creates a fresh Theme Studio view model bound to the shared theme service.</summary>
    public ThemeStudioViewModel CreateThemeStudio() => new(_themes, _liveBackground, _themeAssetStore);

    private void OnUpdateState(UpdateState state)
    {
        UpdateAvailable = state.Available;
        if (!string.IsNullOrEmpty(state.Version))
            UpdateVersion = state.Version;

        IsCheckingForUpdate = state.Phase == UpdatePhase.Checking;
        IsDownloadingUpdate = state.Phase == UpdatePhase.Downloading;
        IsInstallingUpdate = state.Phase == UpdatePhase.Installing;
        DownloadProgress = state.DownloadProgress;

        if (!string.IsNullOrEmpty(state.TransientMessage))
        {
            UpdateMessage = state.TransientMessage;
            _updateMessageTimer?.Dispose();
            _updateMessageTimer = Observable.Timer(TimeSpan.FromSeconds(5))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateMessage = null);
        }
    }

    /// <summary>Creates a fresh song editor (new song). Wire <see cref="SongEditorViewModel.Saved"/> to refresh the library.</summary>
    public SongEditorViewModel CreateSongEditor() => new(_contentLibrary, _themes);
    public NoteEditorViewModel CreateNoteEditor(string heading, string title, string body, NoteSplitMode splitMode) =>
        new(_themes, heading, title, body, splitMode);

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

    /// <summary>Raised when the operator chooses Sign in from local mode; the app layer opens sign-in.</summary>
    public event Action? SignInRequested;

    private bool _isSignedIn;
    /// <summary>True when running on a real cloud session (show "Sign out"); false in local mode (show "Sign in").</summary>
    public bool IsSignedIn { get => _isSignedIn; private set => this.RaiseAndSetIfChanged(ref _isSignedIn, value); }

    /// <summary>Updates the displayed account/seat status after sign-in.</summary>
    public void SetAccount(string organizationName, string branchName, int seatsUsed, int seatCount)
    {
        IsSignedIn = !string.IsNullOrWhiteSpace(organizationName);
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

    // --- In-app paywall (binds to the resolved entitlements) ---

    private const string UpgradeUrl = "https://lumencueapp.com/pricing";

    private EntitlementState Ent => _entitlements.Current;

    /// <summary>Top-bar status strip (trial countdown / grace warning / inactive) — empty when hidden.</summary>
    public bool ShowEntitlementBanner => Ent.HasBanner;
    public string EntitlementBannerText => Ent.BannerText;
    public bool ShowUpgrade => Ent.ShowUpgrade;

    /// <summary>Feature gate: video / motion backgrounds + lower-thirds (Pro and above).</summary>
    public bool CanUseVideoBackgrounds => Ent.CanUseVideoBackgrounds;
    public bool VideoBackgroundsLocked => !Ent.CanUseVideoBackgrounds;
    public bool CanUseSharedLibrary => Ent.CanUseSharedLibrary;

    /// <summary>Usage gate: AI listening allowed (included, active, allowance not exhausted).</summary>
    public bool CanUseAi => Ent.CanUseAi;
    public bool AiBlocked => !Ent.CanUseAi;
    public bool AiNearLimit => Ent.AiNearLimit;

    public string AiUsageText
    {
        get
        {
            if (Ent.IsUnlimitedAi) return "AI listening: unlimited";
            if (!Ent.AiIncluded) return "AI listening is not included on this plan";
            if (Ent.AiExhausted) return "Monthly AI limit reached — resets next month, or upgrade for more";
            return $"AI listening: {Ent.AiMinutesRemaining} of {Ent.AiMinutesAllowance} min left this month";
        }
    }

    /// <summary>True whenever AI listening is part of the plan, so the persistent top-bar minutes
    /// chip stays visible at all times (not only while listening or near the limit).</summary>
    public bool ShowAiMinutes => Ent.AiIncluded || Ent.IsUnlimitedAi;

    /// <summary>Compact, always-on top-bar label for AI minutes remaining this month.</summary>
    public string AiMinutesShort
    {
        get
        {
            if (Ent.IsUnlimitedAi) return "AI: unlimited";
            if (Ent.AiExhausted) return "AI: limit reached";
            return $"AI: {Ent.AiMinutesRemaining} min left";
        }
    }

    public bool IsUpgradePromptOpen
    {
        get => _isUpgradePromptOpen;
        set => this.RaiseAndSetIfChanged(ref _isUpgradePromptOpen, value);
    }

    /// <summary>"What's new" panel shown once after the app updates to a new version.</summary>
    public bool IsWhatsNewOpen
    {
        get => _isWhatsNewOpen;
        set => this.RaiseAndSetIfChanged(ref _isWhatsNewOpen, value);
    }

    public string WhatsNewTitle { get; private set; } = "";
    public ObservableCollection<string> WhatsNewItems { get; } = [];

    public string UpgradePromptTitle
    {
        get => _upgradePromptTitle;
        set => this.RaiseAndSetIfChanged(ref _upgradePromptTitle, value);
    }

    public string UpgradePromptMessage
    {
        get => _upgradePromptMessage;
        set => this.RaiseAndSetIfChanged(ref _upgradePromptMessage, value);
    }

    private void RequestUpgrade(string? feature)
    {
        (UpgradePromptTitle, UpgradePromptMessage) = feature switch
        {
            FeatureKeys.VideoBackgrounds => (
                "Upgrade to use video backgrounds",
                "Video & motion backgrounds and lower-thirds are part of the Pro plan. Upgrade to bring cinematic backgrounds into your services."),
            FeatureKeys.SharedLibrary => (
                "Upgrade to share your library",
                "Sharing a song library across branches is part of the Pro plan. Upgrade to keep every campus in sync."),
            "ai" => (
                "Monthly AI limit reached",
                "You've used this month's AI-listening allowance. It resets at the start of next month — or upgrade to Pro for more hands-free transcription."),
            _ => (
                "Upgrade LumenCue",
                "Unlock more AI-listening minutes, video backgrounds, a shared library and multi-campus management."),
        };
        IsUpgradePromptOpen = true;
    }

    private void OpenCheckout()
    {
        IsUpgradePromptOpen = false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = UpgradeUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open the upgrade page");
            StatusText = "Couldn't open the upgrade page — visit lumencueapp.com/pricing.";
        }
    }

    private void RefreshEntitlements()
    {
        this.RaisePropertyChanged(nameof(ShowEntitlementBanner));
        this.RaisePropertyChanged(nameof(EntitlementBannerText));
        this.RaisePropertyChanged(nameof(ShowUpgrade));
        this.RaisePropertyChanged(nameof(CanUseVideoBackgrounds));
        this.RaisePropertyChanged(nameof(VideoBackgroundsLocked));
        this.RaisePropertyChanged(nameof(CanUseSharedLibrary));
        this.RaisePropertyChanged(nameof(CanUseAi));
        this.RaisePropertyChanged(nameof(AiBlocked));
        this.RaisePropertyChanged(nameof(AiNearLimit));
        this.RaisePropertyChanged(nameof(AiUsageText));
        this.RaisePropertyChanged(nameof(ShowAiMinutes));
        this.RaisePropertyChanged(nameof(AiMinutesShort));
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
            if (value == ParaphrasesTabIndex) HasNewParaphrases = false;
            if (value == SongsTabIndex) _ = SongSearch.RefreshAsync();
            if (value == NotesTabIndex) _ = Notes.LoadAsync();
            this.RaisePropertyChanged(nameof(IsLibraryTab));
            this.RaisePropertyChanged(nameof(IsSuggestionsTab));
            this.RaisePropertyChanged(nameof(IsTopicalTab));
            this.RaisePropertyChanged(nameof(IsParaphrasesTab));
            this.RaisePropertyChanged(nameof(IsSongsTab));
            this.RaisePropertyChanged(nameof(IsNotesTab));
            this.RaisePropertyChanged(nameof(ShowScriptureList));
            this.RaisePropertyChanged(nameof(ShowNowSinging));
            NotifyWorkspaceTabVisibility();
        }
    }

    public bool IsLibraryTab => SelectedContentTab == 0;
    public bool IsSuggestionsTab => SelectedContentTab == SuggestionsTabIndex;
    public bool IsTopicalTab => SelectedContentTab == TopicalTabIndex;
    public bool IsParaphrasesTab => SelectedContentTab == ParaphrasesTabIndex;
    public bool IsSongsTab => SelectedContentTab == SongsTabIndex;
    public bool IsNotesTab => IsNotesMode;

    // ───────────────────────── Top-level workspace mode (Bible vs Songs) ─────────────────────────
    // A service is either displaying scripture or singing. Each mode reconfigures the left sidebar
    // and the center tools. Notes is its own workspace. Themes is a dialog launcher, not a mode.
    private bool _isSongsMode;
    private bool _isMediaMode;
    private bool _isNotesMode;

    public bool IsSongsMode
    {
        get => _isSongsMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSongsMode, value);
            this.RaisePropertyChanged(nameof(IsBibleMode));
            this.RaisePropertyChanged(nameof(IsNotesMode));
            this.RaisePropertyChanged(nameof(ShowBibleMediaSidebar));
            this.RaisePropertyChanged(nameof(LibraryTabLabel));
            this.RaisePropertyChanged(nameof(SearchWatermark));
            this.RaisePropertyChanged(nameof(ShowScriptureList));
            this.RaisePropertyChanged(nameof(ShowNowSinging));
            NotifyWorkspaceTabVisibility();
        }
    }

    // Bible vs Songs is a sub-choice of the "content" workspace; Media is a third top-level workspace
    // that swaps the whole center area for the media bin + transport.
    public bool IsBibleMode => !IsSongsMode && !IsMediaMode && !IsNotesMode;

    /// <summary>Bookmarks / media folders — hidden in Songs and Notes, which have their own sidebars.</summary>
    public bool ShowBibleMediaSidebar => IsBibleMode || IsMediaMode;

    /// <summary>True when the center area shows the Media Playback view instead of the content tabs.</summary>
    public bool IsMediaMode
    {
        get => _isMediaMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isMediaMode, value);
            this.RaisePropertyChanged(nameof(IsBibleMode));
            this.RaisePropertyChanged(nameof(IsNotesMode));
            this.RaisePropertyChanged(nameof(ShowBibleMediaSidebar));
            this.RaisePropertyChanged(nameof(IsContentMode));
            this.RaisePropertyChanged(nameof(ShowScriptureList));
            this.RaisePropertyChanged(nameof(ShowNowSinging));
            NotifyWorkspaceTabVisibility();
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

    /// <summary>Center tab bodies gated by workspace mode so stacked panels never bleed through.</summary>
    public bool ShowSuggestionsTabContent => IsBibleMode && IsSuggestionsTab;
    public bool ShowTopicalTabContent => IsBibleMode && IsTopicalTab;
    public bool ShowParaphrasesTabContent => IsBibleMode && IsParaphrasesTab;
    public bool ShowSongsTabContent => IsSongsMode && IsSongsTab;
    public bool ShowNotesTabContent => IsNotesMode;

    public bool IsNotesMode
    {
        get => _isNotesMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isNotesMode, value);
            this.RaisePropertyChanged(nameof(IsBibleMode));
            this.RaisePropertyChanged(nameof(IsNotesTab));
            this.RaisePropertyChanged(nameof(ShowBibleMediaSidebar));
            NotifyWorkspaceTabVisibility();
        }
    }

    private void NotifyWorkspaceTabVisibility()
    {
        this.RaisePropertyChanged(nameof(ShowSuggestionsTabContent));
        this.RaisePropertyChanged(nameof(ShowTopicalTabContent));
        this.RaisePropertyChanged(nameof(ShowParaphrasesTabContent));
        this.RaisePropertyChanged(nameof(ShowSongsTabContent));
        this.RaisePropertyChanged(nameof(ShowNotesTabContent));
        this.RaisePropertyChanged(nameof(ShowScriptureList));
        this.RaisePropertyChanged(nameof(ShowNowSinging));
    }

    public void EnterBibleMode()
    {
        IsNotesMode = false;
        IsMediaMode = false;
        IsSongsMode = false;
        // Scripture-driven service: keep the AI suggestions panel to scriptures only (no songs).
        _aiMatcher.IncludeContentMatches = false;
        if (SelectedContentTab == SongsTabIndex) SelectedContentTab = TopicalTabIndex;
        _ = ContentSearch.ResetForModeAsync(songsMode: false);
    }

    public void EnterSongsMode()
    {
        IsNotesMode = false;
        IsMediaMode = false;
        IsSongsMode = true;
        _aiMatcher.IncludeContentMatches = true;
        SelectedContentTab = SongsTabIndex;
        _ = ContentSearch.ResetForModeAsync(songsMode: true);
    }

    /// <summary>Switches the center area to the Media Playback view (graphics/videos + transport).</summary>
    public void EnterMediaMode()
    {
        IsNotesMode = false;
        IsSongsMode = false;
        IsMediaMode = true;
    }

    public void EnterNotesMode()
    {
        IsSongsMode = false;
        IsMediaMode = false;
        IsNotesMode = true;
        _ = Notes.LoadAsync();
        StatusText = Notes.HasNotes
            ? "Double-click a note to open its slides."
            : "Add a note, then double-click it to open its slides.";
    }

    public bool HasNewTopical
    {
        get => _hasNewTopical;
        set => this.RaiseAndSetIfChanged(ref _hasNewTopical, value);
    }

    /// <summary>Lights the Paraphrases tab badge when a verse is auto-detected from speech while the
    /// operator is on another tab. Cleared when they open the Paraphrases tab.</summary>
    public bool HasNewParaphrases
    {
        get => _hasNewParaphrases;
        set => this.RaiseAndSetIfChanged(ref _hasNewParaphrases, value);
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

    /// <summary>This Sunday's setlist — songs added from the library before service.</summary>
    public ObservableCollection<Song> SundayPlaylist { get; } = [];

    private Song? _selectedSundaySong;
    public Song? SelectedSundaySong
    {
        get => _selectedSundaySong;
        set => this.RaiseAndSetIfChanged(ref _selectedSundaySong, value);
    }

    public bool HasSundayPlaylist => SundayPlaylist.Count > 0;

    /// <summary>The song loaded into Now Singing (not just the library selection).</summary>
    private Song? _nowSingingSong;

    /// <summary>Slides of the song currently opened in the "Now Singing" tab (Songs mode, tab 0).</summary>
    public ObservableCollection<SongSlideItem> NowSingingSlides { get; } = [];

    private string? _nowSingingTitle;
    public string? NowSingingTitle
    {
        get => _nowSingingTitle;
        private set => this.RaiseAndSetIfChanged(ref _nowSingingTitle, value);
    }

    public bool HasNowSinging => NowSingingSlides.Count > 0;

    /// <summary>Individual slides of the note currently opened in the Notes tab.</summary>
    public ObservableCollection<NotePageSlideItem> NowNoteSlides { get; } = [];

    private string? _openNoteTitle;
    public string? OpenNoteTitle
    {
        get => _openNoteTitle;
        private set => this.RaiseAndSetIfChanged(ref _openNoteTitle, value);
    }

    public bool HasOpenNote => NowNoteSlides.Count > 0;

    private NoteSlideItem? _openNoteCard;
    public NoteSlideItem? OpenNoteCard
    {
        get => _openNoteCard;
        private set => this.RaiseAndSetIfChanged(ref _openNoteCard, value);
    }

    private NotePageSlideItem? _selectedNotePage;
    public NotePageSlideItem? SelectedNotePage
    {
        get => _selectedNotePage;
        set => this.RaiseAndSetIfChanged(ref _selectedNotePage, value);
    }

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
    public ReactiveCommand<Song, Unit> AddToSundayPlaylistCommand { get; }
    public ReactiveCommand<Song, Unit> RemoveFromSundayPlaylistCommand { get; }

    public ReactiveCommand<Unit, Unit> BlankCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleFollowCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleScreenOutputCommand { get; }
    public ReactiveCommand<Unit, Unit> TransitionCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowLibraryTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSuggestionsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSuggestionsCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearResultsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowTopicalTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowParaphrasesTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSongsTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowNotesTabCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowBibleModeCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSongsModeCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowMediaModeCommand { get; }
    public ReactiveCommand<Unit, Unit> SignOutCommand { get; }
    public ReactiveCommand<Unit, Unit> SignInCommand { get; }

    /// <summary>Opens the generic upgrade prompt (from the top-bar Upgrade button / status banner).</summary>
    public ReactiveCommand<Unit, Unit> UpgradeCommand { get; }

    /// <summary>Opens a feature-specific upgrade prompt (param = feature key, e.g. "video_backgrounds" or "ai").</summary>
    public ReactiveCommand<string?, Unit> RequestUpgradeCommand { get; }

    /// <summary>Sends the operator to the hosted checkout / pricing page and closes the prompt.</summary>
    public ReactiveCommand<Unit, Unit> OpenCheckoutCommand { get; }

    public ReactiveCommand<Unit, Unit> DismissUpgradePromptCommand { get; }

    public ReactiveCommand<Unit, Unit> DismissWhatsNewCommand { get; }

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
        IScriptureParaphraseWatcher paraphraseWatcher,
        ISongSearchService songSearch,
        IProPresenterService proPresenter,
        ILiveBackgroundService liveBackground,
        IAnnouncementService announcements,
        ILayerService layers,
        IEntitlementService entitlements,
        IThemeAssetStore themeAssetStore,
        NotesRepository notesRepo,
        IUpdateService? updates = null)
    {
        _projection = projectionService;
        _settings = settings;
        _aiMatcher = aiMatcher;
        _bibleCache = bibleCache;
        _themes = themes;
        _scriptureSearch = scriptureSearch;
        _paraphraseWatcher = paraphraseWatcher;
        _transcriptionService = transcriptionService;
        _contentLibrary = contentLibrary;
        _proPresenter = proPresenter;
        _liveBackground = liveBackground;
        _announcements = announcements;
        _layers = layers;
        _entitlements = entitlements;
        _themeAssetStore = themeAssetStore;
        Backgrounds = new BackgroundsViewModel(liveBackground);
        MediaPlayback = new Operator.MediaPlaybackViewModel(announcements, Outputs);
        CompareCards.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasCompareCards));

        _updates = updates;
        var asmVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        AppVersion = asmVersion is null ? "LumenCue" : $"v{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}";
        OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        RuntimeDescription = $".NET {Environment.Version} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})";
        InstallUpdateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_updates is not null) await _updates.InstallAndRestartAsync();
        });
        CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_updates is not null) await _updates.CheckAsync(userInitiated: true);
        });
        _updates?.State
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnUpdateState);

        ContentSearch = new ContentSearchViewModel(contentLibrary);
        Transcription = new TranscriptionViewModel(transcriptionService, suggestionEngine, projectionService, settings);
        TopicalSearch = new TopicalSearchViewModel(scriptureSearch);
        SongSearch = new SongSearchViewModel(songSearch);
        Notes = new NotesViewModel(notesRepo);
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
        SyncBackgroundSelectionGate();
        _themes.Changed
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                RefreshThemeOptions();
                SyncBackgroundSelectionGate();
            });

        BlankCommand = ReactiveCommand.Create(() => { _projection.GoBlank(); _liveScriptureRef = null; ClearContentLiveHighlights(); foreach (var s in NowSingingSlides) { s.IsLive = false; s.IsSuggested = false; } });
        ToggleFollowCommand = ReactiveCommand.Create(() =>
        {
            FollowMode = FollowMode == LyricFollowMode.Off ? LyricFollowMode.Assist : LyricFollowMode.Off;
        });
        ToggleScreenOutputCommand = ReactiveCommand.Create(() => { ScreenOutputEnabled = !ScreenOutputEnabled; });

        AddToSundayPlaylistCommand = ReactiveCommand.Create<Song>(AddToSundayPlaylist);
        RemoveFromSundayPlaylistCommand = ReactiveCommand.Create<Song>(RemoveFromSundayPlaylist);
        SavePlaylistCommand = ReactiveCommand.Create(SaveCurrentAsPlaylist);
        BeginNamePlaylistCommand = ReactiveCommand.Create(() => { PlaylistsExpanded = true; IsNamingPlaylist = !IsNamingPlaylist; });
        LoadPlaylistCommand = ReactiveCommand.Create<SavedPlaylist>(LoadPlaylist);
        DeletePlaylistCommand = ReactiveCommand.Create<SavedPlaylist>(DeletePlaylist);
        ToggleLibrarySectionCommand = ReactiveCommand.Create(() => { LibraryExpanded = !LibraryExpanded; });
        TogglePlaylistsSectionCommand = ReactiveCommand.Create(() => { PlaylistsExpanded = !PlaylistsExpanded; });
        ShowLibraryTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = 0; });
        ShowSuggestionsTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = SuggestionsTabIndex; });
        ClearSuggestionsCommand = ReactiveCommand.Create(() => { Transcription.Suggestions.Clear(); HasNewSuggestions = false; });
        ClearResultsCommand = ReactiveCommand.Create(() => { ContentSearch.ClearResults(); _preloadedChapter = null; });
        ShowTopicalTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = TopicalTabIndex; });
        ShowParaphrasesTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = ParaphrasesTabIndex; });
        ShowSongsTabCommand = ReactiveCommand.Create(() => { SelectedContentTab = SongsTabIndex; });
        ShowNotesTabCommand = ReactiveCommand.Create(EnterNotesMode);
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

        // Continuous paraphrase detection: while the mic is live, scan finalized utterances for verses
        // the preacher is paraphrasing and surface confident matches in the Find Scripture tab's
        // detected lane. Throttled to settle on whole sentences; the heavy lifting + precision rules
        // live in the watcher. Tied entirely to listening — when Segments stops, so does detection.
        _transcriptionService.Segments
            .Throttle(TimeSpan.FromMilliseconds(600))
            .Subscribe(s => _ = DetectParaphraseAsync(s.Text));

        // "Doesn't exist" warnings → transient toast. Two sources, same UX: spoken references the AI
        // matcher resolved to nothing, and operator-typed references that returned no scripture.
        _aiMatcher.ReferenceNotFound
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(r => ShowToast($"{r.Reference} isn't in the Bible"));

        ContentSearch.InvalidReference
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ShowToast);

        // Reflect mic state in the follow status (e.g. "start the mic to begin" → "Listening…").
        Transcription.WhenAnyValue(x => x.IsListening)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => { if (FollowMode != LyricFollowMode.Off) OnFollowModeChanged(); });

        var canTransition = this.WhenAnyValue<OperatorViewModel, bool>(x => x.HasPreview);
        TransitionCommand = ReactiveCommand.Create(DoTransition, canTransition);

        SyncStatus = DescribeSync(syncScheduler.Status);
        syncScheduler.StatusChanged += info =>
            RxApp.MainThreadScheduler.Schedule(() => SyncStatus = DescribeSync(info));
        SignOutCommand = ReactiveCommand.Create(() => SignOutRequested?.Invoke());
        SignInCommand = ReactiveCommand.Create(() => SignInRequested?.Invoke());

        UpgradeCommand = ReactiveCommand.Create(() => RequestUpgrade(null));
        RequestUpgradeCommand = ReactiveCommand.Create<string?>(RequestUpgrade);
        OpenCheckoutCommand = ReactiveCommand.Create(OpenCheckout);
        DismissUpgradePromptCommand = ReactiveCommand.Create(() => { IsUpgradePromptOpen = false; });
        DismissWhatsNewCommand = ReactiveCommand.Create(() => { IsWhatsNewOpen = false; });

        // Refresh every paywall-bound property whenever entitlements change (sign-in / revalidation).
        _entitlements.Changed += _ => RxApp.MainThreadScheduler.Schedule(RefreshEntitlements);
        RefreshEntitlements();

        Transcription.Suggestions.CollectionChanged += (_, e) =>
        {
            AiSuggestionCount = Transcription.Suggestions.Count;
            if (e.Action == NotifyCollectionChangedAction.Add && SelectedContentTab != SuggestionsTabIndex)
                HasNewSuggestions = true;
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (SuggestionItem item in e.NewItems)
                    StageHeardSuggestion(item);
            }
        };

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
                SyncNotePageLiveRing(pos.Index);
            })
            .DisposeWith(sub);

        _feedSub.Disposable = sub;
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
            HydrateSundayPlaylist();
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
        if (item.IsScripture && string.IsNullOrWhiteSpace(item.Body))
        {
            StatusText = "That verse has no text in this translation yet.";
            return;
        }
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
        if (item.IsScripture) _ = RefreshLiveCompareAsync();
        else CompareCards.Clear();
    }

    /// <summary>Projects a single Now Singing slide and marks it as the one currently on output.</summary>
    public void SendSlideLive(SongSlideItem? slide)
    {
        if (slide is null) return;
        SendItemToLive(slide.Item);
        slide.IsLive = true;
        _lastFollowIndex = NowSingingSlides.IndexOf(slide);
    }

    /// <summary>Projects a saved note deck starting at the first slide.</summary>
    public void SendNoteLive(NoteSlideItem? card)
    {
        if (card is null) return;
        OpenNote(card);
        if (NowNoteSlides.Count > 0)
            SendNotePageLive(NowNoteSlides[0]);
    }

    /// <summary>Opens a saved note and shows its slide breakdown in the Notes tab.</summary>
    public void OpenNote(NoteSlideItem? card)
    {
        if (card is null)
        {
            CloseOpenNote();
            return;
        }

        OpenNoteCard = card;
        OpenNoteTitle = card.Title;
        NowNoteSlides.Clear();

        var theme = _themes.ResolveFor(SlideType.Note);
        IReadOnlyList<string> bodies = card.Note.LinesPerSlide > 0
            ? NoteSlidePlanner.PlanBodies(card.Body, card.Note.SplitMode, card.Note.LinesPerSlide)
            : card.Note.SplitMode == NoteSplitMode.AutoFit
                ? DeckBuilder.BuildNote(card.Title, card.Body, string.Empty, theme, NoteSplitMode.AutoFit)
                    .Slides.Select(s => s.Body).ToList()
                : NoteSlidePlanner.PlanBodies(card.Body, card.Note.SplitMode);

        _suppressNoteLinesApply = true;
        _nowNoteLines = card.Note.LinesPerSlide;
        this.RaisePropertyChanged(nameof(NowNoteLinesPerSlideChoice));
        _suppressNoteLinesApply = false;

        if (bodies.Count == 0)
            bodies = [card.Body];

        for (var i = 0; i < bodies.Count; i++)
            NowNoteSlides.Add(new NotePageSlideItem(card, i, bodies[i], bodies.Count));

        SelectedNotePage = NowNoteSlides.Count > 0 ? NowNoteSlides[0] : null;
        Notes.MarkOpen(card);
        this.RaisePropertyChanged(nameof(HasOpenNote));
        StatusText = $"{card.Title} — {NowNoteSlides.Count} slide{(NowNoteSlides.Count == 1 ? "" : "s")}";
    }

    /// <summary>Writes planned note pages back to the note body, persists, and rebuilds the cards.</summary>
    public async Task ApplyNotePagesAsync(IReadOnlyList<string> pages)
    {
        if (OpenNoteCard is null) return;
        var card = OpenNoteCard;
        card.Note.Body = NoteSlideEdit.Join(pages, card.Note.LinesPerSlide);
        await Notes.PersistAsync(card.Note);
        OpenNote(card);
    }

    /// <summary>Returns to the note library grid from the slide breakdown.</summary>
    public void CloseOpenNote()
    {
        OpenNoteCard = null;
        OpenNoteTitle = null;
        NowNoteSlides.Clear();
        SelectedNotePage = null;
        Notes.MarkOpen(null);
        this.RaisePropertyChanged(nameof(HasOpenNote));
        StatusText = Notes.HasNotes ? "Click a note to open its slides." : "Add a note, then click it to open its slides.";
    }

    /// <summary>Projects one slide from the opened note and marks it live.</summary>
    public void SendNotePageLive(NotePageSlideItem? page)
    {
        if (page is null || OpenNoteCard is null) return;
        var card = OpenNoteCard;
        var theme = _themes.ResolveFor(SlideType.Note);
        var deck = DeckBuilder.BuildNote(card.Title, card.Body, string.Empty, theme, card.Note.SplitMode, card.Note.LinesPerSlide);
        _projection.ProjectDeck(new SlideDeck(deck.Slides, page.Index));
        SetLiveSlideType(SlideType.Note);

        foreach (var slide in NowSingingSlides) { slide.IsLive = false; slide.IsSuggested = false; }
        ClearContentLiveHighlights();
        foreach (var n in Notes.Cards) n.IsLive = false;
        foreach (var p in NowNoteSlides) p.IsLive = false;
        page.IsLive = true;
        card.IsLive = true;
        SelectedNotePage = page;

        _followCooldownUntil = DateTime.UtcNow + FollowCooldown;
        _followPendingTarget = -1;
        _followPendingCount = 0;
        StatusText = $"Live: {card.Title} ({page.Label})";
    }

    private void SyncNotePageLiveRing(int deckIndex)
    {
        if (OpenNoteCard is null || NowNoteSlides.Count == 0 || _liveSlideType != SlideType.Note)
            return;

        for (var i = 0; i < NowNoteSlides.Count; i++)
            NowNoteSlides[i].IsLive = i == deckIndex;
        OpenNoteCard.IsLive = true;
    }

    /// <summary>Steps the live output through the opened note's slides.</summary>
    public void StepNoteLive(int direction)
    {
        if (NowNoteSlides.Count == 0) return;
        var idx = -1;
        for (var i = 0; i < NowNoteSlides.Count; i++)
            if (NowNoteSlides[i].IsLive) { idx = i; break; }

        var next = idx < 0
            ? (direction > 0 ? 0 : NowNoteSlides.Count - 1)
            : Math.Clamp(idx + direction, 0, NowNoteSlides.Count - 1);
        SendNotePageLive(NowNoteSlides[next]);
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

        // Preload the whole chapter into the Scripture tab so the operator can instantly hop to nearby
        // verses the preacher calls out next (e.g. "now give me 34 … 37") without waiting on the AI.
        PreloadChapter(_liveScriptureRef);
    }

    /// <summary>
    /// Preview-only: when a spoken scripture suggestion arrives, show that chapter on the Scripture
    /// grid and highlight the verse. Never goes live — the operator still double-clicks to project.
    /// </summary>
    private void StageHeardSuggestion(SuggestionItem item)
    {
        if (!IsBibleMode || !item.IsScripture) return;

        var reference = ScriptureReferenceParser.TryParse(item.Title)
            ?? TryParseScriptureContentId(item.ContentId);
        if (reference is null) return;

        SelectedContentTab = 0;
        _ = ContentSearch.StageReferenceAsync(reference.Book, reference.Chapter, reference.VerseStart);
    }

    private static ScriptureReference? TryParseScriptureContentId(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId) || !contentId.StartsWith("scripture:", StringComparison.Ordinal))
            return null;
        var parts = contentId.Split(':');
        if (parts.Length < 4) return null;
        if (!int.TryParse(parts[^2], out var chapter) || !int.TryParse(parts[^1], out var verse))
            return null;
        var book = string.Join(':', parts[1..^2]);
        return string.IsNullOrWhiteSpace(book) ? null : new ScriptureReference(book, chapter, verse);
    }

    /// <summary>Loads the chapter behind a just-projected verse into the Scripture tab, quietly (without
    /// stealing the operator's current tab). Skips work if that chapter is already loaded.</summary>
    private void PreloadChapter(ScriptureReference? reference)
    {
        if (reference is null) return;

        var key = (reference.Book, reference.Chapter);
        if (_preloadedChapter == key) return;
        _preloadedChapter = key;

        _ = ContentSearch.LoadFullChapterAsync(reference.Book, reference.Chapter, reference.VerseStart);
    }

    /// <summary>Tracks the live slide's type and refreshes the program-preview theme picker so it
    /// targets (and displays) that type's current theme assignment.</summary>
    private void SetLiveSlideType(SlideType type)
    {
        _liveSlideType = type;
        if (type != SlideType.Scripture)
            _liveScriptureRef = null;
        this.RaisePropertyChanged(nameof(LiveThemeName));
        SyncBackgroundSelectionGate();
        if (type != SlideType.Scripture)
            CompareCards.Clear();
    }

    /// <summary>
    /// Background tiles only highlight when the live theme is a Placeholder. Solid/image themes
    /// already own their backdrop, so a click must not look selected.
    /// </summary>
    private void SyncBackgroundSelectionGate()
    {
        var kind = _themes.ResolveFor(_liveSlideType).BackgroundKind;
        Backgrounds.ThemeAcceptsLiveSelection = ThemeBackgroundResolve.AcceptsLiveSelection(kind);
    }

    /// <summary>Double-click a Now Live compare card to project that translation.</summary>
    public void SendCompareLive(LiveCompareCard? card)
    {
        if (card is null || !card.IsReady) return;
        ContentSearch.SelectedTranslation = card.Translation;
    }

    private bool TrySetCompare(string code, bool selected)
    {
        var already = _compareChosen.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        if (selected == already) return true;
        if (!LiveCompareSelection.Toggle(_compareChosen, code))
        {
            StatusText = "Compare Translations shows 2 — uncheck one first.";
            return false;
        }

        _ = _settings.SetAsync("live_compare_translations", LiveCompareSelection.Format(_compareChosen));
        SyncCompareOptionEnabled();
        _ = RefreshLiveCompareAsync();
        return true;
    }

    private void RebuildCompareOptions()
    {
        CompareOptions.Clear();
        foreach (var code in ContentSearch.AvailableTranslations)
        {
            var on = _compareChosen.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
            CompareOptions.Add(new LiveCompareOption(code, on, TrySetCompare));
        }
        SyncCompareOptionEnabled();
    }

    private void SyncCompareOptionEnabled()
    {
        var atCap = _compareChosen.Count >= LiveCompareSelection.MaxSlots;
        foreach (var option in CompareOptions)
            option.IsEnabled = option.IsSelected || !atCap;
    }

    private async Task RefreshLiveCompareAsync()
    {
        var generation = Interlocked.Increment(ref _compareGeneration);
        var reference = _liveScriptureRef;
        if (reference is null || _liveSlideType != SlideType.Scripture)
        {
            CompareCards.Clear();
            return;
        }

        var codes = LiveCompareSelection.ForDisplay(
            _compareChosen, ContentSearch.SelectedTranslation, ContentSearch.AvailableTranslations);
        if (!codes.SequenceEqual(_compareChosen, StringComparer.OrdinalIgnoreCase))
        {
            _compareChosen.Clear();
            _compareChosen.AddRange(codes);
            _ = _settings.SetAsync("live_compare_translations", LiveCompareSelection.Format(_compareChosen));
            RebuildCompareOptions();
        }

        CompareCards.Clear();
        foreach (var code in codes)
            CompareCards.Add(new LiveCompareCard { Translation = code, Title = code, Body = "Loading…" });

        foreach (var code in codes)
        {
            var loaded = await LoadCompareCardAsync(reference, code).ConfigureAwait(true);
            if (generation != _compareGeneration) return;
            var card = CompareCards.FirstOrDefault(c => c.Translation == code);
            if (card is null || loaded is null) continue;
            card.Title = loaded.Title;
            card.Body = loaded.Body;
            card.Footer = loaded.Footer;
            card.IsReady = loaded.IsReady;
        }
    }

    private async Task<LiveCompareCard> LoadCompareCardAsync(ScriptureReference reference, string translation)
    {
        try
        {
            var verses = await _contentLibrary
                .GetOrFetchVersesAsync(reference, translation, localOnly: false)
                .ConfigureAwait(true);
            if (verses.Count == 0)
            {
                var fallback = await _contentLibrary
                    .GetOrFetchScriptureAsync(reference, translation)
                    .ConfigureAwait(true);
                if (fallback is not null)
                    verses = [fallback];
            }

            if (verses.Count == 0)
                return new LiveCompareCard { Translation = translation, Title = translation, Body = "Not available yet.", IsReady = false };

            var ordered = verses.OrderBy(v => v.VerseStart).ToList();
            var first = ordered[0];
            var last = ordered[^1];
            var label = ordered.Count == 1
                ? first.Reference
                : $"{first.Book} {first.Chapter}:{first.VerseStart}-{last.VerseStart}";
            var body = ordered.Count == 1
                ? first.Text
                : string.Join(" ", ordered.Select(v => v.Text));
            if (string.IsNullOrWhiteSpace(body))
                return new LiveCompareCard { Translation = translation, Title = translation, Body = "No text in this translation.", IsReady = false };

            return new LiveCompareCard
            {
                Translation = translation,
                Title = $"{label} ({translation})",
                Body = body,
                Footer = translation,
                IsReady = true,
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load compare translation {Translation} for {Ref}", translation, reference);
            return new LiveCompareCard { Translation = translation, Title = translation, Body = "Couldn't load.", IsReady = false };
        }
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

        if (ContentSearch.HasRangeSelection)
        {
            var range = ContentSearch.SelectedRangeItems();
            if (range.Count >= 2)
            {
                var bookmark = ScriptureRangeBookmark.FromItems(range);
                Transcription.AddBookmark(bookmark);
                StatusText = $"Bookmarked {bookmark.Title}";
                return;
            }
        }

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
    /// Reloads the verse grid in the new translation, keeps the live highlight on the same verse,
    /// and re-projects that verse so the output does not go blank.
    /// </summary>
    private async Task OnTranslationChangedAsync(string translation, int generation)
    {
        var origin = _liveScriptureRef?.VerseStart ?? 0;
        await ContentSearch.HandleTranslationChangeAsync(origin, _liveScriptureRef);
        RelinkLiveHighlight();
        await RefreshLiveTranslationAsync(translation, generation);
    }

    /// <summary>
    /// After a chapter reload the old live ContentItem is gone from Results. Point IsLive at the
    /// matching new card so next/prev and the ring stay attached to the verse that is on output.
    /// </summary>
    private void RelinkLiveHighlight()
    {
        if (_liveScriptureRef is null) return;

        var idx = IndexOfLiveVerse();
        if (idx < 0) return;

        var item = ContentSearch.Results[idx];
        if (_liveContentItem is not null && !ReferenceEquals(_liveContentItem, item))
            _liveContentItem.IsLive = false;

        _liveContentItem = item;
        item.IsLive = true;
        ContentSearch.SelectedItem = item;
    }

    /// <summary>
    /// Re-renders whatever scripture is currently live into the newly selected translation, so
    /// switching translations updates the verse already on screen — not just the next one.
    /// </summary>
    private async Task RefreshLiveTranslationAsync(string translation, int generation)
    {
        if (_liveSlideType != SlideType.Scripture || _liveScriptureRef is null)
            return;

        var reference = _liveScriptureRef;

        try
        {
            var verses = await _contentLibrary
                .GetOrFetchVersesAsync(reference, translation, localOnly: false)
                .ConfigureAwait(true);
            if (generation != _translationRefreshGeneration)
                return;

            if (verses.Count == 0)
            {
                var fallback = await _contentLibrary
                    .GetOrFetchScriptureAsync(reference, translation)
                    .ConfigureAwait(true);
                if (generation != _translationRefreshGeneration)
                    return;
                if (fallback is not null)
                    verses = [fallback];
            }

            if (verses.Count == 0)
            {
                StatusText = $"{translation} doesn't have {reference} yet — keeping what's on screen";
                return;
            }

            var ordered = verses.OrderBy(v => v.VerseStart).ToList();
            var first = ordered[0];
            var last = ordered[^1];

            var label = ordered.Count == 1
                ? first.Reference
                : $"{first.Book} {first.Chapter}:{first.VerseStart}-{last.VerseStart}";
            var body = ordered.Count == 1
                ? first.Text
                : string.Join(" ", ordered.Select(v => v.Text));
            if (string.IsNullOrWhiteSpace(body))
            {
                StatusText = $"{translation} returned no text for {label} — keeping what's on screen";
                return;
            }

            var footer = $"{label} ({translation})";
            var theme = _themes.ResolveFor(SlideType.Scripture);
            _projection.ProjectDeck(DeckBuilder.Build(SlideType.Scripture, label, body, footer, theme));
            StatusText = $"Switched live to {translation}: {label}";
            _ = RefreshLiveCompareAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh live scripture into {Translation}", translation);
            if (generation == _translationRefreshGeneration)
                StatusText = $"Couldn't load {reference} in {translation} — keeping what's on screen";
        }
    }

    /// <summary>
    /// Pulls the whole chapter behind a scripture suggestion into the Library tab so the operator
    /// can browse and project verses manually without waiting on the live AI.
    /// </summary>
    public void ShowFullChapter(SuggestionItem? item)
    {
        if (item is null) return;
        if (TryParseScriptureId(item.ContentId, out var book, out var chapter, out var verse))
            LoadChapterIntoLibrary(book, chapter, verse);
        else if (TryParseReferenceText(item.Title, out book, out chapter, out var verseFromTitle))
            LoadChapterIntoLibrary(book, chapter, verseFromTitle);
    }

    /// <summary>Loads the entire book behind a bookmark into the Scripture tab.</summary>
    public void ShowFullBook(SuggestionItem? item)
    {
        if (item is null) return;
        if (TryParseScriptureId(item.ContentId, out var book, out var chapter, out var verse))
            LoadBookIntoLibrary(book, chapter, verse);
        else if (TryParseReferenceText(item.Title, out book, out chapter, out var verseFromTitle))
            LoadBookIntoLibrary(book, chapter, verseFromTitle);
    }

    public void ShowFullChapter(ContentItem? item)
    {
        if (item is null) return;

        if (item.Source is ScripturePassage passage)
        {
            LoadChapterIntoLibrary(passage.Book, passage.Chapter, passage.VerseStart);
        }
        else if (TryParseReferenceText(item.Title, out var book, out var chapter, out var verse))
        {
            LoadChapterIntoLibrary(book, chapter, verse);
        }
    }

    private void LoadChapterIntoLibrary(string book, int chapter, int originVerse)
    {
        EnterBibleMode();
        SelectedContentTab = 0;
        StatusText = $"Loading {book} {chapter}...";
        _ = ContentSearch.LoadFullChapterAsync(book, chapter, originVerse);
    }

    private void LoadBookIntoLibrary(string book, int originChapter, int originVerse)
    {
        EnterBibleMode();
        SelectedContentTab = 0;
        StatusText = $"Loading {book}...";
        _ = ContentSearch.LoadFullBookAsync(book, originChapter, originVerse);
    }

    // Runs one finalized utterance through the paraphrase watcher (latest-wins: a fresher utterance
    // cancels an in-flight scan). Confident matches land in the Find Scripture tab's detected lane.
    private async Task DetectParaphraseAsync(string text)
    {
        CancellationToken token;
        try
        {
            _paraphraseCts?.Cancel();
            _paraphraseCts?.Dispose();
            var cts = new CancellationTokenSource();
            _paraphraseCts = cts;
            token = cts.Token;
        }
        catch (ObjectDisposedException) { return; }

        try
        {
            var translation = ContentSearch.SelectedTranslation;
            var detections = await _paraphraseWatcher.DetectAsync(text, translation, token).ConfigureAwait(false);
            if (detections.Count == 0 || token.IsCancellationRequested) return;

            Dispatcher.UIThread.Post(() =>
            {
                TopicalSearch.AddDetections(detections);
                // Nudge the "Paraphrases" tab badge if the operator is looking elsewhere.
                if (SelectedContentTab != ParaphrasesTabIndex)
                    HasNewParaphrases = true;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Live paraphrase detection failed");
        }
    }

    // Routes a spoken utterance to its handler: a translation switch ("...in the King James") or a
    // topical lookup ("find me the scripture about ...").
    private void OnSpokenSegment(string text)
    {
        var translation = SpokenTranslationParser.TryParse(text);
        if (translation is not null)
        {
            ApplySpokenTranslation(translation);
            return;
        }

        var topic = TopicalRequestParser.ExtractTopic(text);
        if (string.IsNullOrWhiteSpace(topic)) return;

        if (SelectedContentTab != TopicalTabIndex)
            HasNewTopical = true;

        _ = TopicalSearch.RunAutoSearchAsync(topic);
    }

    // Switches the active translation in response to a spoken request, re-rendering whatever scripture
    // is already live. Translations the plan doesn't carry (e.g. TPT) tell the operator so out loud.
    private void ApplySpokenTranslation(SpokenTranslationRequest request)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ContentSearch.AvailableTranslations.Contains(request.Code))
            {
                StatusText = $"{request.DisplayName} isn't available — staying on {ContentSearch.SelectedTranslation}";
                return;
            }

            if (string.Equals(ContentSearch.SelectedTranslation, request.Code, StringComparison.OrdinalIgnoreCase))
            {
                StatusText = $"Already showing {request.DisplayName}";
                return;
            }

            ContentSearch.SelectedTranslation = request.Code;
            StatusText = $"Switched to {request.DisplayName}";
        });
    }

    // ContentId shape: "scripture:{Book}:{Chapter}:{VerseStart}"; Book may contain spaces ("1 John").
    private static bool TryParseScriptureId(string contentId, out string book, out int chapter, out int verse)
    {
        book = string.Empty;
        chapter = 0;
        verse = 0;
        if (string.IsNullOrEmpty(contentId)) return false;

        var parts = contentId.Split(':');
        if (parts.Length < 4 || !parts[0].Equals("scripture", StringComparison.OrdinalIgnoreCase))
            return false;

        book = parts[1];
        int.TryParse(parts[3], out verse);
        return int.TryParse(parts[2], out chapter) && chapter > 0 && book.Length > 0;
    }

    private static bool TryParseReferenceText(string reference, out string book, out int chapter, out int verse)
    {
        var parsed = ScriptureReferenceParser.TryParse(reference);
        if (parsed is not null)
        {
            book = parsed.Book;
            chapter = parsed.Chapter;
            verse = parsed.VerseStart;
            return true;
        }
        book = string.Empty;
        chapter = 0;
        verse = 0;
        return false;
    }

    /// <summary>
    /// Arrow-right: page the live deck; on the last page of a verse, go to the next verse
    /// in the loaded chapter. Does not wrap back to verse 1.
    /// </summary>
    public void AdvanceForward()
    {
        if (_projection.MoveNext())
            return;
        AdvanceLiveVerse(+1);
    }

    /// <summary>
    /// Arrow-left: page back through the live deck; on the first page, go to the previous verse.
    /// </summary>
    public void AdvanceBackward()
    {
        if (_projection.MovePrev())
            return;
        AdvanceLiveVerse(-1);
    }

    private void AdvanceLiveVerse(int direction)
    {
        var results = ContentSearch.Results;
        if (results.Count == 0) return;

        var liveIdx = IndexOfLiveVerse();
        var next = VerseAdvance.StepIndex(liveIdx, results.Count, direction);
        if (next < 0 || next == liveIdx) return;

        var item = results[next];
        ContentSearch.SelectedItem = item;
        SendItemToLive(item);
    }

    private int IndexOfLiveVerse()
    {
        var results = ContentSearch.Results;
        if (_liveContentItem is not null)
        {
            var i = results.IndexOf(_liveContentItem);
            if (i >= 0) return i;
        }

        if (_liveScriptureRef is not null)
        {
            for (var i = 0; i < results.Count; i++)
            {
                var r = ReferenceFor(results[i]);
                if (r is not null
                    && string.Equals(r.Book, _liveScriptureRef.Book, StringComparison.OrdinalIgnoreCase)
                    && r.Chapter == _liveScriptureRef.Chapter
                    && r.VerseStart == _liveScriptureRef.VerseStart)
                    return i;
            }
        }

        return -1;
    }

    private void DoTransition()
    {
        SendItemToLive(ContentSearch.SelectedItem);
    }

    private const string PlaylistsKey = "playlists";

    /// <summary>Saves the currently open song as a named playlist (Songs mode setlist).</summary>
    private void SaveCurrentAsPlaylist()
    {
        if (SelectedLibrarySong is null || !HasNowSinging)
        {
            StatusText = "Open a song, then tap + to save it as a playlist.";
            return;
        }

        var items = SongToItems(SelectedLibrarySong)
            .Select(item => new QueueSlide
            {
                Title = item.Title,
                Body = item.Body,
                Footer = item.Footer,
                Tag = item.Tag,
                SlideType = item.Type.ToSlideType(),
                LinesPerSlide = item.LinesPerSlide,
            })
            .ToList();

        if (items.Count == 0)
        {
            StatusText = "That song has no slides to save.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewPlaylistName)
            ? SelectedLibrarySong.Title
            : NewPlaylistName.Trim();

        var existing = Playlists.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Playlists.Remove(existing);

        Playlists.Insert(0, new SavedPlaylist { Name = name, Items = items });
        NewPlaylistName = string.Empty;
        IsNamingPlaylist = false;
        this.RaisePropertyChanged(nameof(HasPlaylists));
        StatusText = $"Saved playlist: {name}";
        _ = PersistPlaylistsAsync();
    }

    /// <summary>Opens a song from the sidebar Library into the "Now Singing" tab as projectable slide
    /// cards. Double-clicking a slide there is what actually sends a section live.</summary>
    public void OpenSong(Song? song)
    {
        if (song is null) return;
        SelectedLibrarySong = song;
        _nowSingingSong = song;
        RebuildNowSingingSlides(song);
        NowSingingTitle = string.IsNullOrWhiteSpace(song.Artist) ? song.Title : $"{song.Title}  ·  {song.Artist}";
        _nowSingingLines = song.LinesPerSlide;
        this.RaisePropertyChanged(nameof(NowSingingLinesPerSlide));
        this.RaisePropertyChanged(nameof(HasNowSinging));
        SelectedContentTab = 0;
        StatusText = $"Opened \"{song.Title}\" — double-click a slide to project it";
    }

    /// <summary>Inserts a section after <paramref name="after"/> (or after the selected card, or at
    /// the end), persists, and reloads Now Singing so the new slide is immediately editable.</summary>
    public async Task AddNowSingingSlideAsync(string sectionType, string text, SongSection? after = null)
    {
        if (_nowSingingSong is null) return;
        after ??= SelectedNowSingingSlide?.Section;
        var created = SongSlideInsert.After(_nowSingingSong.Sections, after, sectionType, text);
        await SaveSongEditAsync(_nowSingingSong);
        SelectedNowSingingSlide = NowSingingSlides.FirstOrDefault(s => ReferenceEquals(s.Section, created))
            ?? NowSingingSlides.FirstOrDefault(s => s.Section.Text == created.Text && s.Section.SectionType == created.SectionType);
    }

    /// <summary>Rebuilds the Now Singing cards so each card equals one projected slide: sections are
    /// paginated exactly as they'll appear on screen (honouring the song's lines-per-slide breaking).</summary>
    private void RebuildNowSingingSlides(Song song)
    {
        ResetFollowState();
        NowSingingSlides.Clear();
        var theme = _themes.ResolveFor(SlideType.Lyric);
        var verse = 0;
        foreach (var section in song.Sections)
        {
            var baseLabel = section.SectionType == "verse"
                ? $"Verse {++verse}"
                : section.Label;
            var pages = DeckBuilder.Build(SlideType.Lyric, "", section.Text, "", theme, song.LinesPerSlide).Slides;
            var multi = pages.Count > 1;
            for (var i = 0; i < pages.Count; i++)
            {
                var label = multi ? $"{baseLabel} ({i + 1})" : baseLabel;
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

    private int _nowNoteLines;
    private bool _suppressNoteLinesApply;

    public IReadOnlyList<string> NoteLinesPerSlideChoices => SongLinesPerSlide.Choices;

    /// <summary>Lines-per-slide for the opened note. Changing it re-pages the cards and persists.</summary>
    public string NowNoteLinesPerSlideChoice
    {
        get => SongLinesPerSlide.ToChoice(_nowNoteLines);
        set
        {
            var lines = SongLinesPerSlide.FromChoice(value);
            if (_nowNoteLines == lines) return;
            _nowNoteLines = lines;
            this.RaisePropertyChanged(nameof(NowNoteLinesPerSlideChoice));
            if (!_suppressNoteLinesApply)
                ApplyNowNoteLines(lines);
        }
    }

    private void ApplyNowNoteLines(int lines)
    {
        if (OpenNoteCard is null) return;
        OpenNoteCard.Note.LinesPerSlide = lines;
        OpenNote(OpenNoteCard);
        _ = Notes.PersistAsync(OpenNoteCard.Note);
        StatusText = lines == 0
            ? $"{OpenNoteCard.Title} — auto-fit / split mode"
            : $"{OpenNoteCard.Title} — {lines} line{(lines == 1 ? "" : "s")} per slide";
    }

    private void ApplyNowSingingLines(int lines)
    {
        var song = _nowSingingSong ?? SelectedLibrarySong;
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

    /// <summary>Opens the playlist's song in Now Singing (Songs mode setlist).</summary>
    public void LoadPlaylist(SavedPlaylist? playlist)
    {
        if (playlist is null || playlist.Items.Count == 0) return;
        EnterSongsMode();
        var title = playlist.Items[0].Title;
        var song = LibrarySongs.FirstOrDefault(s =>
            s.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (song is null)
        {
            StatusText = $"Playlist \"{playlist.Name}\" — song not in the library";
            return;
        }

        OpenSong(song);
        StatusText = $"Loaded playlist: {playlist.Name}";
    }
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
        // Title is just the song name; the section (Verse 5 / Chorus …) is carried in Tag and
        // surfaced as a separate badge, so the live label and projector don't read "Song — Verse 5".
        Title = song.Title,
        Subtitle = song.Artist ?? "",
        Body = section.Text,
        Tag = section.Label,
        Footer = song.Title,
        LinesPerSlide = song.LinesPerSlide,
        Source = song,
    };

    public void DeletePlaylist(SavedPlaylist? playlist)
    {
        if (playlist is null) return;
        Playlists.Remove(playlist);
        this.RaisePropertyChanged(nameof(HasPlaylists));
        _ = PersistPlaylistsAsync();
    }

    public void AddToSundayPlaylist(Song? song)
    {
        if (song is null) return;
        var titles = SundayPlaylist.Select(s => s.Title).ToList();
        if (!SundaySetlist.TryAdd(titles, song.Title))
        {
            StatusText = $"\"{song.Title}\" is already on the Sunday playlist";
            return;
        }

        SundayPlaylist.Add(song);
        this.RaisePropertyChanged(nameof(HasSundayPlaylist));
        PersistSundayPlaylist();
        StatusText = $"Added \"{song.Title}\" to Sunday playlist";
    }

    public void RemoveFromSundayPlaylist(Song? song)
    {
        if (song is null) return;
        var match = SundayPlaylist.FirstOrDefault(s =>
            s.Title.Equals(song.Title, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        SundayPlaylist.Remove(match);
        this.RaisePropertyChanged(nameof(HasSundayPlaylist));
        PersistSundayPlaylist();
    }

    public void OpenSundayPlaylistSong(Song? song)
    {
        if (song is null) return;
        OpenSong(song);
        SelectedSundaySong = song;
    }

    private void HydrateSundayPlaylist()
    {
        var saved = Playlists.FirstOrDefault(p =>
            p.Name.Equals(SundaySetlist.StorageName, StringComparison.OrdinalIgnoreCase));
        SundayPlaylist.Clear();
        if (saved is null)
        {
            this.RaisePropertyChanged(nameof(HasSundayPlaylist));
            return;
        }

        foreach (var item in saved.Items)
        {
            var song = LibrarySongs.FirstOrDefault(s =>
                s.Title.Equals(item.Title, StringComparison.OrdinalIgnoreCase));
            if (song is not null) SundayPlaylist.Add(song);
        }
        this.RaisePropertyChanged(nameof(HasSundayPlaylist));
    }

    private void PersistSundayPlaylist()
    {
        var existing = Playlists.FirstOrDefault(p =>
            p.Name.Equals(SundaySetlist.StorageName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Playlists.Remove(existing);

        Playlists.Insert(0, new SavedPlaylist
        {
            Name = SundaySetlist.StorageName,
            Items = SundayPlaylist.Select(s => new QueueSlide
            {
                Title = s.Title,
                Footer = s.Artist ?? "",
                SlideType = SlideType.Lyric,
            }).ToList(),
        });
        this.RaisePropertyChanged(nameof(HasPlaylists));
        _ = PersistPlaylistsAsync();
    }

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

        _autoStartListening = await _settings.GetBoolAsync("auto_start_listening", false);
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

        var savedCompare = await _settings.GetAsync("live_compare_translations");
        _compareChosen.Clear();
        foreach (var code in LiveCompareSelection.Parse(string.IsNullOrWhiteSpace(savedCompare) ? "MSG,AMP" : savedCompare))
            _compareChosen.Add(code);
        RebuildCompareOptions();

        _aiMatcher.CurrentTranslation = ContentSearch.SelectedTranslation;
        // Match the AI suggestion scope to the current workspace mode (Bible = scripture only).
        _aiMatcher.IncludeContentMatches = IsSongsMode;
        TopicalSearch.Translation = ContentSearch.SelectedTranslation;

        ContentSearch.WhenAnyValue<ContentSearchViewModel, string>(x => x.SelectedTranslation)
            .Skip(1)
            .Subscribe(t =>
            {
                _aiMatcher.CurrentTranslation = t;
                TopicalSearch.Translation = t;
                _ = _settings.SetAsync("bible_translation", t);
                _ = CacheTranslationAsync(t);
                var generation = Interlocked.Increment(ref _translationRefreshGeneration);
                _ = OnTranslationChangedAsync(t, generation);
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

        await MaybeShowWhatsNewAsync();
    }

    private const string LastSeenVersionKey = "app.last_seen_version";

    // Shows the "What's new" panel once after the app updates to a new version. Stays quiet on a
    // fresh install (no prior version recorded) and when the running version has no release notes.
    private async Task MaybeShowWhatsNewAsync()
    {
        try
        {
            var current = AppVersion; // e.g. "v0.7.11"; "LumenCue" when the assembly version is missing
            if (string.IsNullOrWhiteSpace(current) || current == "LumenCue") return;

            var lastSeen = await _settings.GetAsync(LastSeenVersionKey);
            if (lastSeen == current) return; // already shown for this version

            // Record the current version now so the panel never reappears for it, even on a fresh install.
            await _settings.SetAsync(LastSeenVersionKey, current);

            // First launch ever (no record): don't interrupt with notes for a version they never ran before.
            if (string.IsNullOrWhiteSpace(lastSeen)) return;

            var notes = ReleaseNotes.ForVersion(current);
            if (notes is null) return;

            WhatsNewItems.Clear();
            foreach (var n in notes) WhatsNewItems.Add(n);
            WhatsNewTitle = $"What's new in {current}";
            this.RaisePropertyChanged(nameof(WhatsNewTitle));
            IsWhatsNewOpen = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to evaluate What's New panel");
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
