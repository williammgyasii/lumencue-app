using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.Services;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>A media routing target shown in the "Send to" picker: a single screen, or All screens.</summary>
public sealed record MediaTargetOption(string Key, string Name);

/// <summary>
/// A folder shown in the media bin's sidebar. <see cref="IsAll"/> is the synthetic "All media" view;
/// a null <see cref="Id"/> with IsAll=false is "Uncategorized" (media not filed into any folder); any
/// other Id is a real user collection.
/// </summary>
public sealed record MediaFolderOption(string? Id, string Name, bool IsAll = false);

/// <summary>
/// The Media Playback bin (a content tab, like Songs/Scripture). Shows every graphic/video at a glance;
/// clicking a tile sends it live to the chosen <b>target</b> — "All screens" by default, or a single screen
/// (e.g. the lobby TV) picked from the dropdown or a tile's right-click menu. When a video is live, transport
/// controls (play/pause, scrub, skip, volume) drive that target's clip. A short poll keeps the scrubber in sync.
/// </summary>
public sealed class MediaPlaybackViewModel : ReactiveObject, IDisposable
{
    private readonly IAnnouncementService _service;
    private readonly ObservableCollection<OutputRow> _outputs;
    private readonly CompositeDisposable _subs = new();
    private readonly Dictionary<OutputRow, IDisposable> _rowSubs = new();
    private readonly DispatcherTimer _poll;

    // target key -> live media id (mirrors the service so tiles can show where they're live).
    private readonly Dictionary<string, string> _liveByTarget = new();

    // The full, unfiltered library; Items is this filtered down to the selected folder.
    private readonly List<AnnouncementMedia> _allItems = [];

    // One tile (and its decoded thumbnail) per media id, reused across folder switches. Disposing a
    // thumbnail Bitmap that Skia is still drawing causes a native crash, so we never dispose on filter
    // changes — only when the media is actually removed from the library.
    private readonly Dictionary<string, MediaTileViewModel> _tileCache = new();

    private AudioOutputOption? _selectedAudioDevice;
    private MediaTargetOption? _selectedTarget;
    private MediaFolderOption _selectedFolder;
    private string _newFolderName = string.Empty;

    private bool _hasLiveOnTarget;
    private bool _hasAnyLive;
    private bool _hasVideoLive;
    private bool _isPaused;
    private bool _updatingFromPlayer;
    private double _positionFraction;
    private string _positionText = "0:00";
    private string _durationText = "0:00";
    private int _volume = 100;

    public ObservableCollection<MediaTileViewModel> Items { get; } = [];

    /// <summary>The "Send to" targets: "All screens" first, then each active screen by name.</summary>
    public ObservableCollection<MediaTargetOption> Targets { get; } = [];

    /// <summary>Folder sidebar: "All media", "Uncategorized", then each user collection.</summary>
    public ObservableCollection<MediaFolderOption> Folders { get; } = [];

    /// <summary>Audio output devices for video sound.</summary>
    public IReadOnlyList<AudioOutputOption> AudioDevices { get; }

    public ReactiveCommand<MediaTileViewModel, Unit> SelectCommand { get; }
    public ReactiveCommand<MediaTileViewModel, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearAllCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipBackCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateFolderCommand { get; }

    public MediaPlaybackViewModel(IAnnouncementService service, ObservableCollection<OutputRow> outputs)
    {
        _service = service;
        _outputs = outputs;
        AudioDevices = service.AudioDevices;
        _selectedAudioDevice = AudioDevices.FirstOrDefault(d => d.Id == service.AudioDeviceId)
                               ?? AudioDevices.FirstOrDefault();

        SelectCommand = ReactiveCommand.Create<MediaTileViewModel>(t => _service.Select(TargetKey, t.Model));
        RemoveCommand = ReactiveCommand.CreateFromTask<MediaTileViewModel>(t => _service.RemoveAsync(t.Model));
        StopCommand = ReactiveCommand.Create(() => _service.Select(TargetKey, null));
        ClearAllCommand = ReactiveCommand.Create(() =>
        {
            // Pull media off every target at once (All-screens plus any per-screen overrides).
            foreach (var key in _liveByTarget.Keys.ToList())
                _service.Select(key, null);
        });
        PlayPauseCommand = ReactiveCommand.Create(() => _service.SetPaused(TargetKey, !_isPaused));
        SkipBackCommand = ReactiveCommand.Create(() => _service.SkipSeconds(TargetKey, -10));
        SkipForwardCommand = ReactiveCommand.Create(() => _service.SkipSeconds(TargetKey, 10));
        CreateFolderCommand = ReactiveCommand.CreateFromTask(CreateFolderAsync);

        _selectedFolder = new MediaFolderOption(null, "All media", IsAll: true);
        RebuildFolders(_service.Collections);

        RebuildTargets();
        _outputs.CollectionChanged += OnOutputsChanged;

        _service.ItemsChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Rebuild)
            .DisposeWith(_subs);

        _service.CollectionsChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(RebuildFolders)
            .DisposeWith(_subs);

        _service.LiveChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e =>
            {
                if (e.Item is null) _liveByTarget.Remove(e.Target);
                else _liveByTarget[e.Target] = e.Item.Id;
                RefreshLiveStates();
            })
            .DisposeWith(_subs);

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _poll.Tick += (_, _) => PollTransport();
        _poll.Start();
    }

    /// <summary>The screen (or All screens) tiles are sent to / cleared from, and whose transport is shown.</summary>
    public MediaTargetOption? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTarget, value);
            RefreshLiveStates();
            PollTransport();
        }
    }

    private string TargetKey => _selectedTarget?.Key ?? MediaTarget.AllScreens;

    /// <summary>The folder currently shown in the bin. Changing it re-filters <see cref="Items"/> and
    /// decides which folder freshly-imported media lands in.</summary>
    public MediaFolderOption SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (value is null) return;
            this.RaiseAndSetIfChanged(ref _selectedFolder, value);
            ApplyFilter();
        }
    }

    /// <summary>Name typed into the "New folder" box before creating a collection.</summary>
    public string NewFolderName
    {
        get => _newFolderName;
        set => this.RaiseAndSetIfChanged(ref _newFolderName, value);
    }

    private async Task CreateFolderAsync()
    {
        var name = (NewFolderName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        var created = await _service.CreateCollectionAsync(name);
        NewFolderName = string.Empty;

        // Jump into the new folder so the operator can immediately import into it.
        SelectedFolder = Folders.FirstOrDefault(f => f.Id == created.Id) ?? _selectedFolder;
    }

    /// <summary>Sends a tile to a specific target (used by the per-tile right-click menu).</summary>
    public void SendTileTo(MediaTileViewModel? tile, string targetKey)
    {
        if (tile is not null) _service.Select(targetKey, tile.Model);
    }

    /// <summary>Files a tile into a folder (or back to "All media" when <paramref name="collectionId"/>
    /// is null). Used by the per-tile right-click "Move to folder" menu.</summary>
    public Task MoveTileToFolder(MediaTileViewModel? tile, string? collectionId) =>
        tile is null ? Task.CompletedTask : _service.MoveToCollectionAsync(tile.Model.Id, collectionId);

    /// <summary>The chosen audio output device; setting it routes announcement sound there immediately.</summary>
    public AudioOutputOption? SelectedAudioDevice
    {
        get => _selectedAudioDevice;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAudioDevice, value);
            _service.SetAudioDevice(value?.Id ?? string.Empty);
        }
    }

    /// <summary>True when the selected target has anything live (image or video).</summary>
    public bool HasLiveOnTarget
    {
        get => _hasLiveOnTarget;
        private set => this.RaiseAndSetIfChanged(ref _hasLiveOnTarget, value);
    }

    /// <summary>True when any screen (any target) has media live — enables the global Clear.</summary>
    public bool HasAnyLive
    {
        get => _hasAnyLive;
        private set => this.RaiseAndSetIfChanged(ref _hasAnyLive, value);
    }

    /// <summary>True when the selected target has a video live (enables scrub/volume/play-pause).</summary>
    public bool HasVideoLive
    {
        get => _hasVideoLive;
        private set => this.RaiseAndSetIfChanged(ref _hasVideoLive, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isPaused, value);
            this.RaisePropertyChanged(nameof(PlayPauseGlyph));
        }
    }

    /// <summary>Play/pause button glyph (▶ when paused, ❚❚ when playing).</summary>
    public string PlayPauseGlyph => _isPaused ? "\u25B6" : "\u23F8";

    private string _nowPlaying = string.Empty;

    /// <summary>Human-readable summary of what media is live and where, for the always-on media bar
    /// (e.g. "welcome.mp4 · All screens"). Empty when nothing is live.</summary>
    public string NowPlaying
    {
        get => _nowPlaying;
        private set => this.RaiseAndSetIfChanged(ref _nowPlaying, value);
    }

    /// <summary>Normalized scrub position 0..1 (two-way: dragging seeks the live clip).</summary>
    public double PositionFraction
    {
        get => _positionFraction;
        set
        {
            this.RaiseAndSetIfChanged(ref _positionFraction, value);
            if (!_updatingFromPlayer)
                _service.Seek(TargetKey, value);
        }
    }

    public string PositionText
    {
        get => _positionText;
        private set => this.RaiseAndSetIfChanged(ref _positionText, value);
    }

    public string DurationText
    {
        get => _durationText;
        private set => this.RaiseAndSetIfChanged(ref _durationText, value);
    }

    /// <summary>Playback volume 0..100 for the selected target.</summary>
    public int Volume
    {
        get => _volume;
        set
        {
            this.RaiseAndSetIfChanged(ref _volume, value);
            _service.SetVolume(TargetKey, value);
        }
    }

    /// <summary>Adds a media file picked by the view's file dialog, into the selected folder (or
    /// Uncategorized when "All media" is selected). Duplicates are skipped by the service.</summary>
    public Task AddAsync(string path) => _service.AddAsync(path, _selectedFolder.IsAll ? null : _selectedFolder.Id);

    private void PollTransport()
    {
        var status = _service.ReadPlayback(TargetKey);

        HasLiveOnTarget = status is not null;
        HasVideoLive = status is { IsVideo: true };

        if (status is { IsVideo: true })
        {
            IsPaused = status.IsPaused;

            _updatingFromPlayer = true;
            PositionFraction = status.Position;
            _updatingFromPlayer = false;

            PositionText = FormatMs(status.TimeMs);
            DurationText = FormatMs(status.LengthMs);
        }
        else
        {
            PositionText = "0:00";
            DurationText = "0:00";
        }
    }

    private static string FormatMs(long ms)
    {
        if (ms <= 0) return "0:00";
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes}:{t.Seconds:D2}";
    }

    private void OnOutputsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTargets();

    /// <summary>Rebuilds the "Send to" list: All screens + each active physical/windowed screen, keeping
    /// the current selection where possible. Re-subscribes to each screen's active/name changes.</summary>
    private void RebuildTargets()
    {
        foreach (var d in _rowSubs.Values) d.Dispose();
        _rowSubs.Clear();
        foreach (var row in _outputs.Where(o => o.Kind is OutputKind.Display or OutputKind.Windowed))
            _rowSubs[row] = row.WhenAnyValue(r => r.IsActive, r => r.Name)
                .Skip(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => RebuildTargets());

        var keep = _selectedTarget?.Key ?? MediaTarget.AllScreens;

        Targets.Clear();
        Targets.Add(new MediaTargetOption(MediaTarget.AllScreens, "All screens"));
        foreach (var row in _outputs.Where(o => o.Kind is OutputKind.Display or OutputKind.Windowed && o.IsActive))
            Targets.Add(new MediaTargetOption(row.Key, row.Name));

        SelectedTarget = Targets.FirstOrDefault(t => t.Key == keep) ?? Targets[0];
        RefreshLiveStates();
    }

    private void Rebuild(IReadOnlyList<AnnouncementMedia> items)
    {
        _allItems.Clear();
        _allItems.AddRange(items);

        // Add tiles for genuinely new media; collect tiles whose media was removed so we can dispose
        // them *after* they leave the visual tree (disposing a live bitmap segfaults Skia).
        var liveIds = new HashSet<string>(items.Select(i => i.Id));
        foreach (var i in items)
            if (!_tileCache.ContainsKey(i.Id))
                _tileCache[i.Id] = new MediaTileViewModel(i);

        var removed = _tileCache.Keys.Where(id => !liveIds.Contains(id)).ToList();
        foreach (var id in removed)
        {
            var tile = _tileCache[id];
            _tileCache.Remove(id);
            tile.Dispose(); // retires the thumbnail safely (after the next render commit)
        }

        ApplyFilter();
    }

    /// <summary>Rebuilds <see cref="Items"/> from the full library, keeping only media in the selected
    /// folder ("All media" shows everything; "Uncategorized" shows media with no folder). Reuses cached
    /// tiles — it never disposes thumbnails — so switching folders can't race the renderer.</summary>
    private void ApplyFilter()
    {
        IEnumerable<AnnouncementMedia> visible = _selectedFolder.IsAll
            ? _allItems
            : _allItems.Where(i => i.CollectionId == _selectedFolder.Id);

        Items.Clear();
        foreach (var i in visible)
            if (_tileCache.TryGetValue(i.Id, out var tile))
                Items.Add(tile);
        RefreshLiveStates();
    }

    /// <summary>Rebuilds the folder sidebar (All media, Uncategorized, then each user collection),
    /// preserving the current selection where possible.</summary>
    private void RebuildFolders(IReadOnlyList<MediaCollection> collections)
    {
        var keepId = _selectedFolder?.Id;
        var keepAll = _selectedFolder?.IsAll ?? true;

        Folders.Clear();
        Folders.Add(new MediaFolderOption(null, "All media", IsAll: true));
        Folders.Add(new MediaFolderOption(null, "Uncategorized"));
        foreach (var c in collections)
            Folders.Add(new MediaFolderOption(c.Id, c.Name));

        var restored = keepAll
            ? Folders[0]
            : Folders.FirstOrDefault(f => !f.IsAll && f.Id == keepId) ?? Folders[0];

        // Set the backing field directly (avoid re-filtering twice) then refresh once.
        _selectedFolder = restored;
        this.RaisePropertyChanged(nameof(SelectedFolder));
        ApplyFilter();
    }

    private void RefreshLiveStates()
    {
        HasAnyLive = _liveByTarget.Count > 0;

        var names = Targets.ToDictionary(t => t.Key, t => t.Name);
        var selKey = TargetKey;

        string TargetName(string key) =>
            key == MediaTarget.AllScreens ? "All screens" : names.TryGetValue(key, out var n) ? n : key;

        foreach (var t in Items)
        {
            var liveOn = _liveByTarget
                .Where(kv => kv.Value == t.Model.Id)
                .Select(kv => TargetName(kv.Key))
                .ToList();

            t.IsLive = _liveByTarget.TryGetValue(selKey, out var id) && id == t.Model.Id;
            t.LiveOn = liveOn.Count == 0 ? string.Empty : "LIVE: " + string.Join(", ", liveOn);
        }

        // Summary for the always-on media bar: "<name> · <target>" per live channel.
        NowPlaying = string.Join("    •    ", _liveByTarget.Select(kv =>
        {
            var media = _allItems.FirstOrDefault(i => i.Id == kv.Value);
            return $"{media?.Name ?? "media"} · {TargetName(kv.Key)}";
        }));
    }

    public void Dispose()
    {
        _poll.Stop();
        _outputs.CollectionChanged -= OnOutputsChanged;
        foreach (var d in _rowSubs.Values) d.Dispose();
        _rowSubs.Clear();
        Items.Clear();
        foreach (var t in _tileCache.Values) t.Dispose();
        _tileCache.Clear();
        _subs.Dispose();
    }
}

/// <summary>One thumbnail tile in the media bin.</summary>
public sealed class MediaTileViewModel : ReactiveObject, IDisposable
{
    private bool _isLive;
    private string _liveOn = string.Empty;

    public MediaTileViewModel(AnnouncementMedia model)
    {
        Model = model;

        if (model.Kind == AnnouncementMediaKind.Image && File.Exists(model.Path))
        {
            try
            {
                using var fs = File.OpenRead(model.Path);
                Thumbnail = Bitmap.DecodeToWidth(fs, 320);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not build media thumbnail for {Path}", model.Path);
            }
        }
    }

    public AnnouncementMedia Model { get; }
    public string Name => Model.Name;
    public bool IsVideo => Model.Kind == AnnouncementMediaKind.Video;
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;

    public bool IsLive
    {
        get => _isLive;
        set => this.RaiseAndSetIfChanged(ref _isLive, value);
    }

    public string LiveOn
    {
        get => _liveOn;
        set
        {
            this.RaiseAndSetIfChanged(ref _liveOn, value);
            this.RaisePropertyChanged(nameof(IsLiveAnywhere));
        }
    }

    public bool IsLiveAnywhere => !string.IsNullOrEmpty(_liveOn);

    // Retire the thumbnail after the next render commit — disposing a bound bitmap mid-paint segfaults
    // Skia (see SafeBitmapDisposal). Matters because tiles are disposed when media is removed.
    public void Dispose() => SafeBitmapDisposal.Retire(Thumbnail);
}
