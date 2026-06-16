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

    private AudioOutputOption? _selectedAudioDevice;
    private MediaTargetOption? _selectedTarget;

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

    /// <summary>Audio output devices for video sound.</summary>
    public IReadOnlyList<AudioOutputOption> AudioDevices { get; }

    public ReactiveCommand<MediaTileViewModel, Unit> SelectCommand { get; }
    public ReactiveCommand<MediaTileViewModel, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearAllCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipBackCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipForwardCommand { get; }

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

        RebuildTargets();
        _outputs.CollectionChanged += OnOutputsChanged;

        _service.ItemsChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Rebuild)
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

    /// <summary>Sends a tile to a specific target (used by the per-tile right-click menu).</summary>
    public void SendTileTo(MediaTileViewModel? tile, string targetKey)
    {
        if (tile is not null) _service.Select(targetKey, tile.Model);
    }

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

    /// <summary>Adds a media file picked by the view's file dialog.</summary>
    public Task AddAsync(string path) => _service.AddAsync(path);

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
        foreach (var t in Items) t.Dispose();
        Items.Clear();
        foreach (var i in items) Items.Add(new MediaTileViewModel(i));
        RefreshLiveStates();
    }

    private void RefreshLiveStates()
    {
        HasAnyLive = _liveByTarget.Count > 0;

        var names = Targets.ToDictionary(t => t.Key, t => t.Name);
        var selKey = TargetKey;

        foreach (var t in Items)
        {
            var liveOn = _liveByTarget
                .Where(kv => kv.Value == t.Model.Id)
                .Select(kv => kv.Key == MediaTarget.AllScreens
                    ? "All screens"
                    : names.TryGetValue(kv.Key, out var n) ? n : kv.Key)
                .ToList();

            t.IsLive = _liveByTarget.TryGetValue(selKey, out var id) && id == t.Model.Id;
            t.LiveOn = liveOn.Count == 0 ? string.Empty : "LIVE: " + string.Join(", ", liveOn);
        }
    }

    public void Dispose()
    {
        _poll.Stop();
        _outputs.CollectionChanged -= OnOutputsChanged;
        foreach (var d in _rowSubs.Values) d.Dispose();
        _rowSubs.Clear();
        foreach (var t in Items) t.Dispose();
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

    public void Dispose() => Thumbnail?.Dispose();
}
