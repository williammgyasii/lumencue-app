using System;
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
using Serilog;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Default <see cref="ILiveBackgroundService"/>. Persists the library to settings, loads still images
/// directly, and drives motion clips through <see cref="VideoFramePlayerFactory"/>. Only one clip plays
/// at a time; switching disposes the previous player.
/// </summary>
public sealed class LiveBackgroundService : ILiveBackgroundService, IDisposable
{
    private const string LibraryKey = "live_backgrounds_json";
    private const string SelectedKey = "live_background_selected";

    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".m4v", ".webm", ".mkv", ".avi"];

    private readonly SettingsRepository _settings;
    private readonly List<LiveBackground> _items = [];
    private readonly BehaviorSubject<IReadOnlyList<LiveBackground>> _itemsChanged;
    private readonly BehaviorSubject<LiveBackground?> _selectedChanged = new(null);
    private readonly BehaviorSubject<Bitmap?> _frame = new(null);

    private IVideoFramePlayer? _player;
    private Bitmap? _imageBitmap;

    public LiveBackgroundService(SettingsRepository settings)
    {
        _settings = settings;
        _itemsChanged = new BehaviorSubject<IReadOnlyList<LiveBackground>>(Snapshot());
    }

    public IReadOnlyList<LiveBackground> Items => Snapshot();
    public IObservable<IReadOnlyList<LiveBackground>> ItemsChanged => _itemsChanged;
    public LiveBackground? Selected => _selectedChanged.Value;
    public IObservable<LiveBackground?> SelectedChanged => _selectedChanged;
    public IObservable<Bitmap?> Frame => _frame;

    public async Task LoadAsync()
    {
        try
        {
            var json = await _settings.GetAsync(LibraryKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var saved = JsonSerializer.Deserialize<List<LiveBackground>>(json);
                if (saved is not null)
                {
                    _items.Clear();
                    _items.AddRange(saved.Where(i => !string.IsNullOrWhiteSpace(i.Path)));
                }
            }

            _itemsChanged.OnNext(Snapshot());

            var selectedId = await _settings.GetAsync(SelectedKey);
            var selected = _items.FirstOrDefault(i => i.Id == selectedId);
            if (selected is not null) Select(selected);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load live backgrounds");
        }
    }

    public async Task<LiveBackground?> AddAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var item = new LiveBackground
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
            Kind = VideoExtensions.Contains(ext) ? LiveBackgroundKind.Video : LiveBackgroundKind.Image,
        };

        _items.Add(item);
        _itemsChanged.OnNext(Snapshot());
        await PersistAsync();
        return item;
    }

    public async Task RemoveAsync(LiveBackground item)
    {
        if (!_items.Remove(item)) return;
        if (ReferenceEquals(Selected, item) || Selected?.Id == item.Id) Select(null);
        _itemsChanged.OnNext(Snapshot());
        await PersistAsync();
    }

    public void Select(LiveBackground? item)
    {
        // Tear down whatever is currently playing.
        _player?.Dispose();
        _player = null;
        _imageBitmap?.Dispose();
        _imageBitmap = null;

        _selectedChanged.OnNext(item);
        _ = _settings.SetAsync(SelectedKey, item?.Id ?? string.Empty);

        if (item is null)
        {
            _frame.OnNext(null);
            return;
        }

        if (!File.Exists(item.Path))
        {
            Log.Warning("Live background file missing: {Path}", item.Path);
            _frame.OnNext(null);
            return;
        }

        if (item.Kind == LiveBackgroundKind.Video)
        {
            _player = VideoFramePlayerFactory.Start(
                new VideoPlayRequest(item.Path, Loop: true, Audio: false),
                bmp => _frame.OnNext(bmp));
            if (!_player.IsRunning)
            {
                _player.Dispose();
                _player = null;
                _frame.OnNext(null);
            }
        }
        else
        {
            try
            {
                _imageBitmap = new Bitmap(item.Path);
                _frame.OnNext(_imageBitmap);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load image background {Path}", item.Path);
                _frame.OnNext(null);
            }
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            await _settings.SetAsync(LibraryKey, JsonSerializer.Serialize(_items));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist live backgrounds");
        }
    }

    private List<LiveBackground> Snapshot() => [.. _items];

    public void Dispose()
    {
        _player?.Dispose();
        _imageBitmap?.Dispose();
    }
}
