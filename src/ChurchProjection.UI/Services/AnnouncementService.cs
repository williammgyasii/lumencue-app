using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ChurchProjection.Core.Models.Projection;
using ChurchProjection.Infrastructure.Data;
using ChurchProjection.UI.Services.Video;
using ChurchProjection.UI.ViewModels.Operator;
using Serilog;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Default <see cref="IAnnouncementService"/>. Persists the library + chosen audio device to settings,
/// loads still graphics directly and drives video clips through <see cref="VideoFramePlayerFactory"/>.
/// Announcements are tracked PER CHANNEL: each channel owns its own slot (current item, player or image,
/// and frame stream), so several channels can run different announcements simultaneously. Live selections
/// are intentionally NOT restored on startup, so launching the app never blasts audio unexpectedly.
/// </summary>
public sealed class AnnouncementService : IAnnouncementService, IDisposable
{
    private const string LibraryKey = "announcements_json";
    private const string CollectionsKey = "media_collections_json";
    private const string AudioDeviceKey = "announcement_audio_device";

    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".m4v", ".webm", ".mkv", ".avi"];

    private readonly SettingsRepository _settings;
    private readonly List<AnnouncementMedia> _items = [];
    private readonly List<MediaCollection> _collections = [];
    private readonly BehaviorSubject<IReadOnlyList<AnnouncementMedia>> _itemsChanged;
    private readonly BehaviorSubject<IReadOnlyList<MediaCollection>> _collectionsChanged;
    private readonly Subject<(string, AnnouncementMedia?)> _liveChanged = new();
    private readonly ConcurrentDictionary<string, ChannelSlot> _slots = new();

    private string _audioDeviceId = string.Empty;

    public AnnouncementService(SettingsRepository settings)
    {
        _settings = settings;
        _itemsChanged = new BehaviorSubject<IReadOnlyList<AnnouncementMedia>>(Snapshot());
        _collectionsChanged = new BehaviorSubject<IReadOnlyList<MediaCollection>>(CollectionsSnapshot());
        AudioDevices = VideoFramePlayerFactory.EnumerateAudioDevices();
    }

    public IReadOnlyList<AnnouncementMedia> Items => Snapshot();
    public IObservable<IReadOnlyList<AnnouncementMedia>> ItemsChanged => _itemsChanged;
    public IReadOnlyList<MediaCollection> Collections => CollectionsSnapshot();
    public IObservable<IReadOnlyList<MediaCollection>> CollectionsChanged => _collectionsChanged;
    public IObservable<(string Target, AnnouncementMedia? Item)> LiveChanged => _liveChanged;
    public IReadOnlyList<AudioOutputOption> AudioDevices { get; }
    public string AudioDeviceId => _audioDeviceId;

    public async Task LoadAsync()
    {
        try
        {
            var json = await _settings.GetAsync(LibraryKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var saved = JsonSerializer.Deserialize<List<AnnouncementMedia>>(json);
                if (saved is not null)
                {
                    _items.Clear();
                    _items.AddRange(saved.Where(i => !string.IsNullOrWhiteSpace(i.Path)));
                }
            }
            _itemsChanged.OnNext(Snapshot());

            var collectionsJson = await _settings.GetAsync(CollectionsKey);
            if (!string.IsNullOrWhiteSpace(collectionsJson))
            {
                var savedCollections = JsonSerializer.Deserialize<List<MediaCollection>>(collectionsJson);
                if (savedCollections is not null)
                {
                    _collections.Clear();
                    _collections.AddRange(savedCollections.Where(c => !string.IsNullOrWhiteSpace(c.Name)));
                }
            }
            _collectionsChanged.OnNext(CollectionsSnapshot());

            _audioDeviceId = await _settings.GetAsync(AudioDeviceKey) ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load announcements");
        }
    }

    public async Task<AnnouncementMedia?> AddAsync(string path, string? collectionId = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        // Don't duplicate a file that's already in the library — reuse the existing tile instead.
        var existing = MediaLibrary.FindByPath(_items, path);
        if (existing is not null) return existing;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var item = new AnnouncementMedia
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
            Kind = VideoExtensions.Contains(ext) ? AnnouncementMediaKind.Video : AnnouncementMediaKind.Image,
            CollectionId = string.IsNullOrWhiteSpace(collectionId) ? null : collectionId,
        };

        _items.Add(item);
        _itemsChanged.OnNext(Snapshot());
        await PersistAsync();
        return item;
    }

    public async Task<MediaCollection> CreateCollectionAsync(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) trimmed = "New folder";

        // Reuse an existing folder with the same name (case-insensitive) rather than creating a twin.
        var existing = _collections.FirstOrDefault(
            c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var collection = new MediaCollection { Name = trimmed };
        _collections.Add(collection);
        _collectionsChanged.OnNext(CollectionsSnapshot());
        await PersistCollectionsAsync();
        return collection;
    }

    public async Task DeleteCollectionAsync(string collectionId)
    {
        if (string.IsNullOrWhiteSpace(collectionId)) return;

        var removed = _collections.RemoveAll(c => c.Id == collectionId);
        if (removed == 0) return;

        var moved = false;
        foreach (var item in _items)
        {
            if (item.CollectionId != collectionId) continue;
            item.CollectionId = null;
            moved = true;
        }

        _collectionsChanged.OnNext(CollectionsSnapshot());
        await PersistCollectionsAsync();
        if (moved)
        {
            _itemsChanged.OnNext(Snapshot());
            await PersistAsync();
        }
    }

    public async Task MoveToCollectionAsync(string mediaId, string? collectionId)
    {
        var item = _items.FirstOrDefault(i => i.Id == mediaId);
        if (item is null) return;

        var target = string.IsNullOrWhiteSpace(collectionId) ? null : collectionId;
        if (item.CollectionId == target) return;

        item.CollectionId = target;
        _itemsChanged.OnNext(Snapshot());
        await PersistAsync();
    }

    public async Task RemoveAsync(AnnouncementMedia item)
    {
        if (!_items.Remove(item)) return;

        // Clear it from any channel where it is currently live.
        foreach (var kv in _slots.ToArray())
            if (kv.Value.Current?.Id == item.Id)
                Select(kv.Key, null);

        _itemsChanged.OnNext(Snapshot());
        await PersistAsync();
    }

    public AnnouncementMedia? GetLive(string target) =>
        _slots.TryGetValue(Key(target), out var slot) ? slot.Current : null;

    public IObservable<Bitmap?> FrameFor(string screenKey)
    {
        var key = Key(screenKey);
        var allFrame = GetSlot(MediaTarget.AllScreens).Frame;

        // The All-screens target shows only its own stream; an individual screen shows its
        // screen-specific media when set, otherwise falls back to whatever is sent to All.
        if (key == MediaTarget.AllScreens) return allFrame;

        return Observable
            .CombineLatest(GetSlot(key).Frame, allFrame, (own, all) => own ?? all)
            .DistinctUntilChanged();
    }

    public void Select(string target, AnnouncementMedia? item)
    {
        var id = Key(target);
        var slot = GetSlot(id);

        slot.Player?.Dispose();
        slot.Player = null;
        slot.Image?.Dispose();
        slot.Image = null;
        slot.Current = item;

        _liveChanged.OnNext((id, item));

        if (item is null)
        {
            slot.Frame.OnNext(null);
            return;
        }

        if (!File.Exists(item.Path))
        {
            Log.Warning("Announcement file missing: {Path}", item.Path);
            slot.Frame.OnNext(null);
            return;
        }

        if (item.Kind == AnnouncementMediaKind.Video)
        {
            var request = AnnouncementPlayback.RequestFor(item, _audioDeviceId);
            if (request is null)
            {
                slot.Frame.OnNext(null);
                return;
            }

            slot.Player = VideoFramePlayerFactory.Start(
                request,
                bmp => slot.Frame.OnNext(bmp));
            if (slot.Player.IsRunning)
            {
                slot.Player.Volume = slot.Volume;
            }
            else
            {
                slot.Player.Dispose();
                slot.Player = null;
                slot.Frame.OnNext(null);
            }
        }
        else
        {
            try
            {
                slot.Image = new Bitmap(item.Path);
                slot.Frame.OnNext(slot.Image);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load announcement image {Path}", item.Path);
                slot.Frame.OnNext(null);
            }
        }
    }

    public void SetAudioDevice(string deviceId)
    {
        _audioDeviceId = deviceId ?? string.Empty;
        foreach (var slot in _slots.Values)
            slot.Player?.SetAudioDevice(_audioDeviceId);
        _ = _settings.SetAsync(AudioDeviceKey, _audioDeviceId);
    }

    public void SetPaused(string target, bool paused)
    {
        if (_slots.TryGetValue(Key(target), out var slot))
            slot.Player?.SetPaused(paused);
    }

    public void Seek(string target, double fraction)
    {
        if (_slots.TryGetValue(Key(target), out var slot) && slot.Player is { } p)
            p.Position = (float)Math.Clamp(fraction, 0, 1);
    }

    public void SkipSeconds(string target, double seconds)
    {
        if (_slots.TryGetValue(Key(target), out var slot) && slot.Player is { LengthMs: > 0 } p)
        {
            var to = p.TimeMs + (long)(seconds * 1000);
            p.Position = (float)Math.Clamp((double)to / p.LengthMs, 0, 1);
        }
    }

    public void SetVolume(string target, int volume)
    {
        var slot = GetSlot(Key(target));
        slot.Volume = Math.Clamp(volume, 0, 100);
        if (slot.Player is { } p) p.Volume = slot.Volume;
    }

    public PlaybackStatus? ReadPlayback(string target)
    {
        if (!_slots.TryGetValue(Key(target), out var slot) || slot.Current is null) return null;

        if (slot.Player is { } p)
            return new PlaybackStatus(true, p.IsPaused, p.Position, p.TimeMs, p.LengthMs, p.Volume);

        // A still image is live: no transport, but report it so the UI can show a Stop control.
        return new PlaybackStatus(false, false, 0, 0, 0, slot.Volume);
    }

    private ChannelSlot GetSlot(string id) => _slots.GetOrAdd(id, _ => new ChannelSlot());

    private static string Key(string? target) =>
        string.IsNullOrEmpty(target) ? MediaTarget.AllScreens : target;

    private async Task PersistAsync()
    {
        try
        {
            await _settings.SetAsync(LibraryKey, JsonSerializer.Serialize(_items));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist announcements");
        }
    }

    private async Task PersistCollectionsAsync()
    {
        try
        {
            await _settings.SetAsync(CollectionsKey, JsonSerializer.Serialize(_collections));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist media collections");
        }
    }

    private List<AnnouncementMedia> Snapshot() => [.. _items];

    private List<MediaCollection> CollectionsSnapshot() => [.. _collections];

    public void Dispose()
    {
        foreach (var slot in _slots.Values)
        {
            slot.Player?.Dispose();
            slot.Image?.Dispose();
        }
    }

    /// <summary>Per-channel playback state: what's live, the active player/image, and its frame stream.</summary>
    private sealed class ChannelSlot
    {
        public BehaviorSubject<Bitmap?> Frame { get; } = new(null);
        public IVideoFramePlayer? Player { get; set; }
        public Bitmap? Image { get; set; }
        public AnnouncementMedia? Current { get; set; }
        public int Volume { get; set; } = 100;
    }
}
