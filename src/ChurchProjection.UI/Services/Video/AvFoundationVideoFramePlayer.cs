using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Serilog;

namespace ChurchProjection.UI.Services.Video;

/// <summary>
/// Mac decoder: P/Invoke into liblumenvideo.dylib (AVPlayer + BGRA frames).
/// Uses the same 3-buffer WriteableBitmap rotation as the LibVLC players.
/// </summary>
internal sealed class AvFoundationVideoFramePlayer : IVideoFramePlayer
{
    private const int BytesPerPixel = 4;

    private readonly Action<Bitmap> _onFrame;
    private readonly IntPtr _handle;
    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;
    private readonly int _bufferSize;
    private readonly byte[]? _stable;
    private readonly object _stableLock = new();
    private readonly WriteableBitmap[]? _buffers;
    private int _writeIndex;
    private DispatcherTimer? _timer;
    private volatile bool _frameReady;
    private bool _disposed;

    public AvFoundationVideoFramePlayer(VideoPlayRequest request, Action<Bitmap> onFrame)
    {
        _onFrame = onFrame;
        try
        {
            if (!Native.TryEnsureLoaded())
            {
                Log.Warning("liblumenvideo.dylib is not available; motion video will not play on this Mac");
                return;
            }

            _handle = Native.Open(request.Path, request.Loop ? 1 : 0, request.Audio ? 1 : 0,
                request.MaxWidth, request.MaxHeight);
            if (_handle == IntPtr.Zero)
            {
                Log.Warning("AVFoundation failed to open {Path}", request.Path);
                return;
            }

            Native.GetInfo(_handle, out var info);
            _width = info.Width > 0 ? info.Width : request.MaxWidth;
            _height = info.Height > 0 ? info.Height : request.MaxHeight;
            _stride = _width * BytesPerPixel;
            _bufferSize = _stride * _height;
            _stable = new byte[_bufferSize];
            _buffers = [NewBitmap(), NewBitmap(), NewBitmap()];

            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch (DllNotFoundException ex)
        {
            Log.Warning(ex, "liblumenvideo.dylib missing");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AVFoundation player failed to start for {Path}", request.Path);
        }
    }

    public bool IsRunning => _handle != IntPtr.Zero && !_disposed && Native.IsRunning(_handle) != 0;

    public float Position
    {
        get => _handle == IntPtr.Zero ? 0f : Native.GetPosition(_handle);
        set { if (_handle != IntPtr.Zero) Native.SetPosition(_handle, Math.Clamp(value, 0f, 1f)); }
    }

    public long TimeMs => _handle == IntPtr.Zero ? 0 : Native.GetTimeMs(_handle);
    public long LengthMs => _handle == IntPtr.Zero ? 0 : Native.GetLengthMs(_handle);

    public int Volume
    {
        get => _handle == IntPtr.Zero ? 0 : Native.GetVolume(_handle);
        set { if (_handle != IntPtr.Zero) Native.SetVolume(_handle, Math.Clamp(value, 0, 100)); }
    }

    public bool IsPaused => _handle != IntPtr.Zero && Native.IsPaused(_handle) != 0;

    public void SetPaused(bool paused)
    {
        if (_handle != IntPtr.Zero) Native.SetPaused(_handle, paused ? 1 : 0);
    }

    public void SetAudioDevice(string? deviceId)
    {
        // AVPlayer uses the system default output for now.
        if (!string.IsNullOrWhiteSpace(deviceId))
            Log.Debug("Announcement audio device '{Dev}' ignored on Mac (system default)", deviceId);
    }

    private WriteableBitmap NewBitmap() =>
        new(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed || _handle == IntPtr.Zero || _stable is null || _buffers is null) return;

        lock (_stableLock)
        {
            if (Native.CopyFrame(_handle, _stable, _stride, _height) != 0)
                _frameReady = true;
        }

        if (!_frameReady) return;

        var target = _buffers[_writeIndex];
        using (var fb = target.Lock())
        {
            lock (_stableLock)
            {
                if (fb.RowBytes == _stride)
                {
                    Marshal.Copy(_stable, 0, fb.Address, _bufferSize);
                }
                else
                {
                    for (var y = 0; y < _height; y++)
                        Marshal.Copy(_stable, y * _stride, fb.Address + y * fb.RowBytes, _stride);
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
            if (_handle != IntPtr.Zero) Native.Close(_handle);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error stopping AVFoundation player");
        }

        if (_buffers is not null)
        {
            foreach (var bitmap in _buffers)
                SafeBitmapDisposal.Retire(bitmap);
        }
    }

    private static class Native
    {
        private const string Lib = "lumenvideo";
        private static bool _loadAttempted;
        private static bool _loaded;

        public static bool TryEnsureLoaded()
        {
            if (_loadAttempted) return _loaded;
            _loadAttempted = true;
            _loaded = NativeLibrary.TryLoad(Lib, typeof(AvFoundationVideoFramePlayer).Assembly,
                DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories, out _);
            return _loaded;
        }

        [DllImport(Lib, EntryPoint = "lc_video_open", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int loop, int audio,
            int maxWidth, int maxHeight);

        [DllImport(Lib, EntryPoint = "lc_video_is_running", CallingConvention = CallingConvention.Cdecl)]
        public static extern int IsRunning(IntPtr handle);

        [DllImport(Lib, EntryPoint = "lc_video_get_info", CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetInfo(IntPtr handle, out LcVideoInfo info);

        [DllImport(Lib, EntryPoint = "lc_video_copy_frame", CallingConvention = CallingConvention.Cdecl)]
        public static extern int CopyFrame(IntPtr handle, byte[] dest, int destStride, int destHeight);

        [DllImport(Lib, EntryPoint = "lc_video_get_position", CallingConvention = CallingConvention.Cdecl)]
        public static extern float GetPosition(IntPtr handle);

        [DllImport(Lib, EntryPoint = "lc_video_set_position", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPosition(IntPtr handle, float position);

        [DllImport(Lib, EntryPoint = "lc_video_get_time_ms", CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetTimeMs(IntPtr handle);

        [DllImport(Lib, EntryPoint = "lc_video_get_length_ms", CallingConvention = CallingConvention.Cdecl)]
        public static extern long GetLengthMs(IntPtr handle);

        [DllImport(Lib, EntryPoint = "lc_video_get_volume", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetVolume(IntPtr handle);

        [DllImport(Lib, EntryPoint = "lc_video_set_volume", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetVolume(IntPtr handle, int volume);

        [DllImport(Lib, EntryPoint = "lc_video_is_paused", CallingConvention = CallingConvention.Cdecl)]
        public static extern int IsPaused(IntPtr handle);

        [DllImport(Lib, EntryPoint = "lc_video_set_paused", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPaused(IntPtr handle, int paused);

        [DllImport(Lib, EntryPoint = "lc_video_close", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Close(IntPtr handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LcVideoInfo
    {
        public int Width;
        public int Height;
        public int Stride;
    }
}
