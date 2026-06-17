using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Serilog;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Decodes a looping video file into Avalonia <see cref="WriteableBitmap"/> frames using LibVLC's
/// memory-render callbacks (no native VideoView), so projected text composes cleanly on top. Frames
/// rotate through three buffers and are pushed to <paramref name="onFrame"/> on the UI thread at ~30fps. Decoding
/// to memory is CPU-bound by design; a 720p loop keeps the cost reasonable for a background layer.
/// </summary>
internal sealed class VlcBackgroundPlayer : IDisposable
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

    // Held as fields so the unmanaged callbacks are not garbage-collected mid-playback.
    private readonly MediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;

    private volatile bool _frameReady;
    private MediaPlayer? _player;
    private Media? _media;

    // A 3-buffer rotation (rather than a front/back pair): the frame just handed to the UI must not be
    // overwritten on the very next tick, because Avalonia's render thread may still be compositing it.
    // Rotating through three buffers gives any displayed frame two full ticks of slack before reuse,
    // which avoids the NullReferenceException raised inside Image.Render when a bound bitmap is mutated
    // mid-paint.
    private readonly WriteableBitmap[] _buffers;
    private int _writeIndex;
    private DispatcherTimer? _timer;
    private bool _disposed;

    public VlcBackgroundPlayer(string path, Action<Bitmap> onFrame)
    {
        _onFrame = onFrame;
        _lockCb = Lock;
        _displayCb = Display;
        _vlcBuffer = Marshal.AllocHGlobal(BufferSize);
        _buffers = [NewBitmap(), NewBitmap(), NewBitmap()];
        Start(path);
    }

    /// <summary>True if LibVLC native libraries loaded and playback started.</summary>
    public bool IsRunning => _player is not null;

    private static WriteableBitmap NewBitmap() =>
        new(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    private static LibVLC GetLibVlc()
    {
        lock (InitLock)
        {
            if (_libVlc is null)
            {
                LibVLCSharp.Shared.Core.Initialize();
                _libVlc = new LibVLC("--no-audio", "--quiet");
            }
        }
        return _libVlc;
    }

    private void Start(string path)
    {
        try
        {
            var libvlc = GetLibVlc();
            _player = new MediaPlayer(libvlc) { EnableHardwareDecoding = false };
            _media = new Media(libvlc, new Uri(path));
            _media.AddOption(":input-repeat=65535"); // loop indefinitely
            _media.AddOption(":no-audio");
            _player.SetVideoFormat("RV32", Width, Height, Stride);
            _player.SetVideoCallbacks(_lockCb, null, _displayCb);
            _player.Play(_media);

            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Motion background failed to start for {Path}; is LibVLC available?", path);
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
        // Runs on a VLC decode thread between frames: snapshot the freshly decoded frame so the UI
        // timer always copies from a stable buffer.
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
            Log.Debug(ex, "Error stopping motion background player");
        }

        if (_vlcBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_vlcBuffer);

        // A projector window may still hold one of these frames as its Image.Source. Retire them after
        // the next render commit instead of freeing the native surface out from under the compositor.
        foreach (var b in _buffers) SafeBitmapDisposal.Retire(b);
    }
}
