namespace ChurchProjection.UI.Services;

/// <summary>
/// Current OTA update state, surfaced to the UI so it can show a persistent "update available"
/// toast. <see cref="TransientMessage"/> carries one-off feedback (e.g. "You're up to date")
/// for user-initiated checks.
/// </summary>
public sealed record UpdateState(bool Available, string? Version, string? TransientMessage = null);

/// <summary>
/// Abstraction over the OTA updater so view models (in the UI project) can react to update
/// availability and trigger installs without referencing Velopack directly. The concrete
/// implementation lives in the App project.
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

    /// <summary>Downloads the pending update (if any) and restarts the app into the new version.</summary>
    Task InstallAndRestartAsync();
}
