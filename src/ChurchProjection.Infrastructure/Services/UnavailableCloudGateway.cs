using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Safe fallback for builds that have no cloud API configured. It never grants access: every
/// sign-in/validate fails with a clear message, so a build can never silently accept arbitrary
/// credentials. The hosted <see cref="HttpCloudGateway"/> is the only path that grants seats.
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
