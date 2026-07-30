using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Serilog;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Decodes a video into Avalonia <see cref="WriteableBitmap"/> frames via LibVLC memory callbacks
/// (like <see cref="VlcBackgroundPlayer"/>) but with AUDIO ENABLED and routed to a chosen output
/// device — used for announcement clips that must play sound. Uses its own LibVLC instance so the
/// global muted background instance is left untouched.
/// </summary>
internal sealed class VlcMediaPlayer : IDisposable
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int BytesPerPixel = 4;
    private const int Stride = Width * BytesPerPixel;
    private const int BufferSize = Stride * Height;

    private static LibVLC? _libVlc;
    private static readonly object InitLock = new();

    private readonly Action<Bitmap> _onFrame;
    private readonly IntPtr _vlcBuffer;
    private readonly byte[] _stable = new byte[BufferSize];
    private readonly object _stableLock = new();

    private readonly MediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;

    // The audio device to route to once playback (and the audio output module) actually starts.
    private readonly string? _audioDeviceId;

    private volatile bool _frameReady;
    private MediaPlayer? _player;
    private Media? _media;

    // A 3-buffer rotation (rather than a front/back pair): a frame handed to the UI must not be
    // overwritten on the very next tick, since Avalonia's render thread may still be compositing it —
    // especially now that one clip can be displayed on several screens at once. This avoids the
    // NullReferenceException raised inside Image.Render when a bound bitmap is mutated mid-paint.
    private readonly WriteableBitmap[] _buffers;
    private int _writeIndex;
    private DispatcherTimer? _timer;
    private bool _disposed;

    public VlcMediaPlayer(string path, bool loop, string? audioDeviceId, Action<Bitmap> onFrame)
    {
        _onFrame = onFrame;
        _audioDeviceId = audioDeviceId;
        _lockCb = Lock;
        _displayCb = Display;
        _vlcBuffer = Marshal.AllocHGlobal(BufferSize);
        _buffers = [NewBitmap(), NewBitmap(), NewBitmap()];
        Start(path, loop);
    }

    /// <summary>True if LibVLC native libraries loaded and playback started.</summary>
    public bool IsRunning => _player is not null;

    // ----- Transport (valid only while a video player is running) -----

    /// <summary>Normalized playback position 0..1; settable to scrub.</summary>
    public float Position
    {
        get => _player?.Position ?? 0f;
        set { if (_player is not null) _player.Position = Math.Clamp(value, 0f, 1f); }
    }

    /// <summary>Current playback time in milliseconds.</summary>
    public long TimeMs => _player?.Time ?? 0;

    /// <summary>Total clip length in milliseconds (0 until known).</summary>
    public long LengthMs => _player?.Length ?? 0;

    /// <summary>Audio volume 0..100.</summary>
    public int Volume
    {
        get => _player?.Volume ?? 100;
        set { if (_player is not null) _player.Volume = Math.Clamp(value, 0, 100); }
    }

    /// <summary>True while paused (audio + video frozen).</summary>
    public bool IsPaused => _player is not null && !_player.IsPlaying;

    /// <summary>Pause or resume playback.</summary>
    public void SetPaused(bool paused) => _player?.SetPause(paused);

    private static WriteableBitmap NewBitmap() =>
        new(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    private static LibVLC GetLibVlc()
    {
        lock (InitLock)
        {
            if (_libVlc is null)
            {
                LibVlcBootstrap.EnsureInitialized();
                // Audio enabled (no --no-audio) so announcement clips can play sound.
                _libVlc = new LibVLC("--quiet");
            }
        }
        return _libVlc;
    }

    /// <summary>Lists the audio output devices available to LibVLC (empty id = system default first).</summary>
    public static IReadOnlyList<AudioOutputOption> EnumerateAudioDevices()
    {
        var result = new List<AudioOutputOption> { new(string.Empty, "System default") };
        try
        {
            var libvlc = GetLibVlc();
            using var probe = new MediaPlayer(libvlc);
            var devices = probe.AudioOutputDeviceEnum;
            if (devices is not null)
            {
                foreach (var d in devices)
                {
                    if (string.IsNullOrWhiteSpace(d.DeviceIdentifier)) continue;
                    var name = string.IsNullOrWhiteSpace(d.Description) ? d.DeviceIdentifier : d.Description;
                    result.Add(new AudioOutputOption(d.DeviceIdentifier, name));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not enumerate audio output devices");
        }
        return result;
    }

    /// <summary>
    /// Switches the audio output device on the <b>already-playing</b> player. This is safe (unlike
    /// routing at initial play): the audio output module exists once playback is underway. Runs off the
    /// caller thread so a slow native call never blocks the UI, and is guarded against teardown.
    /// </summary>
    public void SetAudioDevice(string? deviceId)
    {
        var dev = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (_disposed || _player is null) return;
                Log.Information("[vlc] live set device -> {Dev}", dev ?? "(default)");
                _player.SetOutputDevice(dev);
                Log.Information("[vlc] live device set ok");
            }
            catch (Exception ex) { Log.Debug(ex, "Failed to set announcement audio device"); }
        });
    }

    private void Start(string path, bool loop)
    {
        try
        {
            var libvlc = GetLibVlc();
            _player = new MediaPlayer(libvlc) { EnableHardwareDecoding = false };
            _media = new Media(libvlc, new Uri(path));
            if (loop) _media.AddOption(":input-repeat=65535");
            _player.SetVideoFormat("RV32", Width, Height, Stride);
            _player.SetVideoCallbacks(_lockCb, null, _displayCb);

            // NOTE: audio currently plays on the system default output. Per-device routing is disabled
            // here on purpose — see SetAudioDevice — because the unguarded SetOutputDevice call segfaults.
            if (!string.IsNullOrWhiteSpace(_audioDeviceId))
                Log.Debug("Announcement audio device '{Dev}' ignored (system default until module-aware routing lands)", _audioDeviceId);

            _player.Play(_media);
            _player.Volume = 100;

            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Announcement video failed to start for {Path}; is LibVLC available?", path);
            _player = null;
        }
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _vlcBuffer);
        return IntPtr.Zero;
    }

    private void Display(IntPtr opaque, IntPtr picture)
    {
        lock (_stableLock)
        {
            Marshal.Copy(_vlcBuffer, _stable, 0, BufferSize);
            _frameReady = true;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed || !_frameReady) return;

        var target = _buffers[_writeIndex];
        using (var fb = target.Lock())
        {
            lock (_stableLock)
            {
                if (fb.RowBytes == Stride)
                {
                    Marshal.Copy(_stable, 0, fb.Address, BufferSize);
                }
                else
                {
                    for (var y = 0; y < Height; y++)
                        Marshal.Copy(_stable, y * Stride, fb.Address + y * fb.RowBytes, Stride);
                }
                _frameReady = false;
            }
        }

        _writeIndex = (_writeIndex + 1) % _buffers.Length;
        _onFrame(target);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTick;

        try
        {
            _player?.Stop();
            _player?.Dispose();
            _media?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error stopping announcement player");
        }

        if (_vlcBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_vlcBuffer);

        // A window may still hold the last frame as its Image.Source; retire after the next render
        // commit rather than freeing the native surface mid-paint.
        foreach (var b in _buffers) SafeBitmapDisposal.Retire(b);
    }
}
