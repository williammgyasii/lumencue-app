using ChurchProjection.UI.ViewModels;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Sends the program feed as an NDI video source (e.g. for OBS, vMix, or ATEM).
/// </summary>
public interface INdiOutputService : IDisposable
{
    /// <summary>True when the native NDI library loaded and a sender can be created.</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable reason when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    /// <summary>Whether frames are currently being sent.</summary>
    bool IsRunning { get; }

    /// <summary>The NDI source name visible on the network.</summary>
    string SourceName { get; set; }

    /// <summary>Starts sending from the given program view at 1920×1080.</summary>
    void Start(ProjectorViewModel programFeed);

    /// <summary>Stops capture and tears down the NDI sender.</summary>
    void Stop();
}
