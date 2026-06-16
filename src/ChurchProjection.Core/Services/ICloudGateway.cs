using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Tenancy;

namespace ChurchProjection.Core.Services;

public sealed record SignInRequest(string OrganizationCode, string BranchCode, string Password, string DeviceId);

public sealed record SignInResult(bool Success, AuthSession? Session, string? Error)
{
    public static SignInResult Ok(AuthSession session) => new(true, session, null);
    public static SignInResult Fail(string error) => new(false, null, error);
}

/// <summary>A page of org songs changed since a cursor, returned by a pull.</summary>
public sealed record SongSyncBatch(IReadOnlyList<Song> Changed, string? Cursor);

/// <summary>
/// The single seam between the desktop app and the cloud. A stub implementation lets the whole
/// sign-in/seat flow run locally; the real implementation talks to the hosted auth/sync API.
/// </summary>
public interface ICloudGateway
{
    /// <summary>True when a real backend is configured (false for the local stub).</summary>
    bool IsConfigured { get; }

    Task<SignInResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);

    /// <summary>Re-checks an existing session (token still valid, seat still held).</summary>
    Task<SignInResult> ValidateAsync(AuthSession session, CancellationToken cancellationToken = default);

    /// <summary>Releases the seat held by this device.</summary>
    Task SignOutAsync(AuthSession session, CancellationToken cancellationToken = default);

    /// <summary>Pulls org songs changed since <paramref name="sinceCursor"/> (Phase 3 sync).</summary>
    Task<SongSyncBatch> PullSongsAsync(string organizationId, string? sinceCursor, CancellationToken cancellationToken = default);

    /// <summary>Pushes locally-changed org songs to the cloud (Phase 3 sync).</summary>
    Task PushSongsAsync(string organizationId, IReadOnlyList<Song> songs, CancellationToken cancellationToken = default);
}
