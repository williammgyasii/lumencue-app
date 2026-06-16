using System.Net.NetworkInformation;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Data;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Reconciles the local song library with the cloud at the organization level. Push sends locally
/// changed rows (dirty), pull applies cloud changes since a stored cursor. Runs on an interval, a
/// debounced trigger after edits, and when the network reconnects. Single-flight; failures are
/// logged and retried on the next tick without disrupting the operator.
/// </summary>
public sealed class SyncScheduler : ISyncScheduler, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    private readonly ICloudGateway _gateway;
    private readonly SongRepository _songs;
    private readonly SettingsRepository _settings;
    private readonly ITenantContext _tenant;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _intervalTimer;
    private readonly Timer _debounceTimer;
    private int _rerun;
    private bool _started;

    public SyncScheduler(ICloudGateway gateway, SongRepository songs, SettingsRepository settings, ITenantContext tenant)
    {
        _gateway = gateway;
        _songs = songs;
        _settings = settings;
        _tenant = tenant;

        _intervalTimer = new Timer(_ => Trigger(), null, Timeout.Infinite, Timeout.Infinite);
        _debounceTimer = new Timer(_ => Trigger(), null, Timeout.Infinite, Timeout.Infinite);

        // Local edits flow in via the repository's change event (debounced into a sync pass).
        _songs.Changed += NotifyLocalChange;
    }

    public bool IsCloudConfigured => _gateway.IsConfigured;

    private SyncStatusInfo _status = SyncStatusInfo.Disabled;
    public SyncStatusInfo Status => _status;
    public event Action<SyncStatusInfo>? StatusChanged;

    public void Start()
    {
        if (!IsCloudConfigured)
        {
            SetStatus(SyncStatusInfo.Disabled);
            return;
        }

        _started = true;
        SetStatus(new SyncStatusInfo(SyncState.Idle, _status.LastSyncUtc, "Connected"));
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _intervalTimer.Change(TimeSpan.Zero, Interval); // immediate first pass, then every Interval
    }

    public void Stop()
    {
        _started = false;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _intervalTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void NotifyLocalChange()
    {
        if (!_started || !IsCloudConfigured) return;
        _debounceTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) Trigger();
    }

    private void Trigger() => _ = SyncNowAsync();

    public async Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCloudConfigured || !_tenant.HasRemoteOrganization)
        {
            SetStatus(SyncStatusInfo.Disabled);
            return new SyncResult(true, 0, 0, "Local only");
        }

        // Single-flight: if a pass is already running, ask it to run once more when it finishes.
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Exchange(ref _rerun, 1);
            return new SyncResult(true, 0, 0, "coalesced");
        }

        try
        {
            SyncResult result;
            do
            {
                Interlocked.Exchange(ref _rerun, 0);
                result = await RunPassAsync(cancellationToken).ConfigureAwait(false);
            }
            while (Interlocked.Exchange(ref _rerun, 0) == 1);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncResult> RunPassAsync(CancellationToken ct)
    {
        var org = _tenant.OrganizationId;
        var cursorKey = $"sync.cursor.songs.{org}";
        SetStatus(new SyncStatusInfo(SyncState.Syncing, _status.LastSyncUtc, "Syncing…"));

        try
        {
            // --- Push local changes -------------------------------------
            var pending = await _songs.GetPendingPushAsync().ConfigureAwait(false);
            if (pending.Count > 0)
            {
                foreach (var song in pending.Where(s => string.IsNullOrWhiteSpace(s.CloudId)))
                {
                    song.CloudId = Guid.NewGuid().ToString();
                    await _songs.SetCloudIdAsync(song.Id, song.CloudId).ConfigureAwait(false);
                }

                await _gateway.PushSongsAsync(org, pending, ct).ConfigureAwait(false);
                await _songs.MarkPushedAsync(pending.Select(s => s.Id)).ConfigureAwait(false);
            }

            // --- Pull cloud changes -------------------------------------
            var cursor = await _settings.GetAsync(cursorKey).ConfigureAwait(false);
            var batch = await _gateway.PullSongsAsync(org, cursor, ct).ConfigureAwait(false);
            var applied = await _songs.ApplyCloudAsync(batch.Changed).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(batch.Cursor) && batch.Cursor != cursor)
                await _settings.SetAsync(cursorKey, batch.Cursor).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            SetStatus(new SyncStatusInfo(SyncState.Idle, now, "Synced"));
            Log.Information("Song sync complete: pushed {Pushed}, pulled {Pulled}", pending.Count, applied);
            return new SyncResult(true, applied, pending.Count);
        }
        catch (Exception ex)
        {
            var offline = ex is HttpRequestException or TaskCanceledException or TimeoutException;
            SetStatus(new SyncStatusInfo(
                offline ? SyncState.Offline : SyncState.Error, _status.LastSyncUtc,
                offline ? "Offline — will retry" : "Sync error"));
            Log.Warning(ex, "Song sync pass failed (will retry)");
            return new SyncResult(false, 0, 0, ex.Message);
        }
    }

    private void SetStatus(SyncStatusInfo status)
    {
        _status = status;
        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        _songs.Changed -= NotifyLocalChange;
        Stop();
        _intervalTimer.Dispose();
        _debounceTimer.Dispose();
        _gate.Dispose();
    }
}
