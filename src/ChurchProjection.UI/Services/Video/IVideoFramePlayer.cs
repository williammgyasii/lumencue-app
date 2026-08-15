namespace ChurchProjection.UI.Services.Video;

/// <summary>
/// OS-specific video decoder that pumps BGRA frames into Avalonia bitmaps.
/// Text overlays and NDI capture the composed feed; this is not a native video widget.
/// </summary>
public interface IVideoFramePlayer : IDisposable
{
    /// <summary>True if the native decoder loaded and playback started.</summary>
    bool IsRunning { get; }

    /// <summary>Normalized playback position 0..1; settable to scrub.</summary>
    float Position { get; set; }

    /// <summary>Current playback time in milliseconds.</summary>
    long TimeMs { get; }

    /// <summary>Total clip length in milliseconds (0 until known).</summary>
    long LengthMs { get; }

    /// <summary>Audio volume 0..100.</summary>
    int Volume { get; set; }

    /// <summary>True while paused (audio + video frozen).</summary>
    bool IsPaused { get; }

    /// <summary>Pause or resume playback.</summary>
    void SetPaused(bool paused);

    /// <summary>Switch the audio output device on an already-playing clip.</summary>
    void SetAudioDevice(string? deviceId);
}

/// <summary>Which native decoder the factory will use.</summary>
public enum VideoFrameEngine
{
    LibVlc,
    AvFoundation,
}

/// <summary>Start options for <see cref="VideoFramePlayerFactory"/>.</summary>
public sealed record VideoPlayRequest(
    string Path,
    bool Loop = true,
    bool Audio = false,
    string? AudioDeviceId = null,
    int MaxWidth = 1280,
    int MaxHeight = 720);
