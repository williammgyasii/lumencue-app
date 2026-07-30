using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ChurchProjection.Core.Models.Theme;
using ChurchProjection.UI.ViewModels;
using ChurchProjection.UI.Views;
using NewTek;
using NewTek.NDI;
using Serilog;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Renders the program <see cref="ProjectorView"/> off-screen and streams BGRA frames over NDI.
/// Requires the native NDI runtime (libndi.dylib on Mac, Processing.NDI.Lib.x64.dll on Windows).
/// </summary>
public sealed class NdiOutputService : INdiOutputService
{
    private const int Width = (int)Theme.CanvasWidth;
    private const int Height = (int)Theme.CanvasHeight;
    private const int FpsNumerator = 30;
    private const int FpsDenominator = 1;
    private const string DefaultSourceName = "LumenCue Program";

    private static readonly object InitLock = new();
    private static int _ndiRefCount;
    private static bool _ndiInitialized;

    private Sender? _sender;
    private VideoFrame? _videoFrame;
    private RenderTargetBitmap? _captureBitmap;
    private byte[]? _captureBuffer;
    private double _captureScale = 1;
    private Control? _captureRoot;
    private Window? _captureWindow;
    private DispatcherTimer? _timer;
    private string _sourceName = DefaultSourceName;
    private bool _running;

    public NdiOutputService()
    {
        ProbeAvailability();
    }

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public bool IsRunning => _running;

    public string SourceName
    {
        get => _sourceName;
        set => _sourceName = string.IsNullOrWhiteSpace(value) ? DefaultSourceName : value.Trim();
    }

    public void Start(ProjectorViewModel programFeed)
    {
        if (_running) Stop();

        if (!EnsureNdiInitialized())
        {
            Log.Warning("NDI output not started: {Reason}", UnavailableReason);
            return;
        }

        try
        {
            _sender = new Sender(SourceName, clockVideo: true, clockAudio: false);
            _videoFrame = new VideoFrame(Width, Height, 16f / 9f, FpsNumerator, FpsDenominator);

            // Avalonia only renders controls attached to a shown window; a bare Measure/Arrange yields black frames.
            _captureWindow = new Window
            {
                Width = Width,
                Height = Height,
                ShowInTaskbar = false,
                SystemDecorations = SystemDecorations.None,
                CanResize = false,
                Background = Brushes.Black,
                Title = "LumenCue NDI",
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(-20000, -20000),
            };
            _captureWindow.Show();
            _captureScale = _captureWindow.RenderScaling;

            // On Retina, Avalonia lays out at 1× inside a 1080p bitmap leaving content in the top-left quarter.
            // Measure at logical size, then scale up to fill the full 1920×1080 canvas.
            var view = new ProjectorView
            {
                DataContext = programFeed,
                Width = Width / _captureScale,
                Height = Height / _captureScale,
            };

            _captureRoot = new LayoutTransformControl
            {
                Width = Width,
                Height = Height,
                LayoutTransform = new ScaleTransform(_captureScale, _captureScale),
                Child = view,
            };

            _captureWindow.Content = new Panel
            {
                Width = Width,
                Height = Height,
                Background = Brushes.Black,
                Children = { _captureRoot },
            };
            _captureWindow.UpdateLayout();

            _captureBitmap = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96));
            _captureBuffer = new byte[Width * 4 * Height];

            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / FpsNumerator), DispatcherPriority.Render, CaptureTick);
            _timer.Start();
            _running = true;

            Log.Information("NDI output started as '{Source}' ({W}×{H} @ {Fps}fps, scale {Scale:0.##}×)",
                SourceName, Width, Height, FpsNumerator, _captureScale);
        }
        catch (Exception ex)
        {
            UnavailableReason = ex.Message;
            IsAvailable = false;
            Log.Error(ex, "Failed to start NDI output");
            Stop();
        }
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();
        _timer = null;
        _captureWindow?.Close();
        _captureWindow = null;
        _captureRoot = null;
        _captureBitmap?.Dispose();
        _captureBitmap = null;
        _captureBuffer = null;
        _videoFrame?.Dispose();
        _videoFrame = null;
        _sender?.Dispose();
        _sender = null;
        ReleaseNdi();
    }

    public void Dispose() => Stop();

    private void CaptureTick(object? sender, EventArgs e)
    {
        if (!_running || _captureRoot is null || _captureBitmap is null || _captureBuffer is null || _videoFrame is null || _sender is null)
            return;

        try
        {
            _captureWindow?.UpdateLayout();
            _captureBitmap.Render(_captureRoot);

            const int stride = Width * 4;
            var handle = GCHandle.Alloc(_captureBuffer, GCHandleType.Pinned);
            try
            {
                _captureBitmap.CopyPixels(
                    new PixelRect(0, 0, Width, Height),
                    handle.AddrOfPinnedObject(),
                    _captureBuffer.Length,
                    stride);

                // Avalonia RTB pixels are premultiplied; NDI expects straight opaque BGRA.
                ConvertToStraightOpaqueBgra(_captureBuffer, Width, Height, stride);

                CopyRows(_captureBuffer, stride, _videoFrame.BufferPtr, _videoFrame.Stride, Height);
            }
            finally
            {
                handle.Free();
            }

            _sender.Send(_videoFrame);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NDI frame capture failed");
        }
    }

    private static void CopyRows(byte[] src, int srcStride, IntPtr dest, int destStride, int rows)
    {
        var rowBytes = Math.Min(srcStride, destStride);
        for (var y = 0; y < rows; y++)
            Marshal.Copy(src, y * srcStride, dest + y * destStride, rowBytes);
    }

    /// <summary>Premultiplied BGRA → straight opaque BGRA for NDI video.</summary>
    private static void ConvertToStraightOpaqueBgra(byte[] pixels, int width, int height, int stride)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                var a = pixels[i + 3];

                if (a == 0)
                {
                    pixels[i] = 0;
                    pixels[i + 1] = 0;
                    pixels[i + 2] = 0;
                }
                else if (a < 255)
                {
                    pixels[i] = (byte)Math.Min(255, b * 255 / a);
                    pixels[i + 1] = (byte)Math.Min(255, g * 255 / a);
                    pixels[i + 2] = (byte)Math.Min(255, r * 255 / a);
                }

                pixels[i + 3] = 255;
            }
        }
    }

    private void ProbeAvailability()
    {
        try
        {
            if (EnsureNdiInitialized())
            {
                IsAvailable = true;
                UnavailableReason = null;
                ReleaseNdi();
            }
        }
        catch (DllNotFoundException ex)
        {
            IsAvailable = false;
            UnavailableReason =
                "NDI runtime not found. Install NDI Tools or the NDI SDK (libndi.dylib / Processing.NDI.Lib.x64.dll).";
            Log.Debug(ex, "NDI runtime missing");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
            Log.Debug(ex, "NDI probe failed");
        }
    }

    private static bool EnsureNdiInitialized()
    {
        lock (InitLock)
        {
            if (_ndiInitialized)
            {
                _ndiRefCount++;
                return true;
            }

            if (!NDIlib.initialize())
            {
                throw new InvalidOperationException("NDIlib.initialize() returned false.");
            }

            _ndiInitialized = true;
            _ndiRefCount = 1;
            return true;
        }
    }

    private static void ReleaseNdi()
    {
        lock (InitLock)
        {
            if (!_ndiInitialized || _ndiRefCount <= 0) return;
            _ndiRefCount--;
            if (_ndiRefCount > 0) return;
            NDIlib.destroy();
            _ndiInitialized = false;
        }
    }
}
