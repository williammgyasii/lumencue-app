namespace ChurchProjection.Core.Services;

public enum SyncState
{
    Disabled,   // no cloud configured (stub / local-only)
    Idle,       // configured, not currently syncing
    Syncing,
    Offline,    // last attempt could not reach the cloud
    Error,
}

/// <summary>Snapshot of the scheduler's state, surfaced to the operator UI.</summary>
public sealed record SyncStatusInfo(SyncState State, DateTime? LastSyncUtc, string Message)
{
    public static SyncStatusInfo Disabled { get; } = new(SyncState.Disabled, null, "Local only");
}

public record SyncResult(bool Success, int ItemsPulled, int ItemsPushed, string? Error = null);

/// <summary>
/// Background reconciler between the local SQLite library and the cloud (org-level songs).
/// Runs on an interval, right after a local edit (debounced), and when connectivity returns.
/// </summary>
public interface ISyncScheduler
{
    bool IsCloudConfigured { get; }

    SyncStatusInfo Status { get; }

    /// <summary>Raised (on a thread-pool thread) whenever <see cref="Status"/> changes.</summary>
    event Action<SyncStatusInfo>? StatusChanged;

    /// <summary>Begins the interval timer + reconnect handling. Safe to call again after Stop.</summary>
    void Start();

    /// <summary>Stops the timer and reconnect handling (e.g. on sign-out).</summary>
    void Stop();

    /// <summary>Requests an immediate (debounced) sync pass — used after a song is saved.</summary>
    void NotifyLocalChange();

    /// <summary>Runs a single reconcile pass now. Single-flight: overlapping calls are coalesced.</summary>
    Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default);
}
