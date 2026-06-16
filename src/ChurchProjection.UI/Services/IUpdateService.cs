namespace ChurchProjection.UI.Services;

/// <summary>Lifecycle of an update check/download, surfaced to the UI.</summary>
public enum UpdatePhase
{
    Idle,
    Checking,
    Available,
    Downloading,
    Installing
}

/// <summary>
/// Current OTA update state. <see cref="TransientMessage"/> carries one-off feedback
/// (e.g. "You're up to date") for user-initiated checks; the view model auto-dismisses it.
/// </summary>
public sealed record UpdateState
{
    public UpdatePhase Phase { get; init; } = UpdatePhase.Idle;
    public string? Version { get; init; }
    public int DownloadProgress { get; init; }
    public string? TransientMessage { get; init; }

    /// <summary>True once an update is found and through the download/install phases.</summary>
    public bool Available => Phase is UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.Installing;
}

/// <summary>
/// Abstraction over the OTA updater so view models (in the UI project) can react to update
/// availability, show download progress, and trigger installs without referencing Velopack
/// directly. The concrete implementation lives in the App project.
/// </summary>
public interface IUpdateService
{
    /// <summary>Pushes the latest <see cref="UpdateState"/>; replays the current value on subscribe.</summary>
    IObservable<UpdateState> State { get; }

    /// <summary>
    /// Checks the release feed for a newer version. When <paramref name="userInitiated"/> is true,
    /// a transient message is emitted even when no update is found (or it can't check).
    /// </summary>
    Task CheckAsync(bool userInitiated = false);

    /// <summary>
    /// Downloads the pending update (reporting progress via <see cref="State"/>), then restarts
    /// the app into the new version. No-ops if no update is pending.
    /// </summary>
    Task InstallAndRestartAsync();
}
