using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Safe fallback for shipped builds that somehow have no cloud API configured. Unlike the dev-only
/// <see cref="StubCloudGateway"/>, it never grants access: every sign-in/validate fails with a clear
/// message. This guarantees a production build can never silently accept arbitrary credentials.
/// </summary>
public sealed class UnavailableCloudGateway : ICloudGateway
{
    private readonly string _reason;

    public UnavailableCloudGateway(string reason) => _reason = reason;

    // Reported as configured so the app gates on a real sign-in rather than offline-grace bypass.
    public bool IsConfigured => true;

    public Task<SignInResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignInResult.Fail(_reason));

    public Task<SignInResult> ValidateAsync(AuthSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignInResult.Fail(_reason));

    public Task SignOutAsync(AuthSession session, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<SongSyncBatch> PullSongsAsync(string organizationId, string? sinceCursor, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SongSyncBatch(Array.Empty<Song>(), sinceCursor));

    public Task PushSongsAsync(string organizationId, IReadOnlyList<Song> songs, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
