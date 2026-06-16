using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Local development gateway used when no cloud API is configured. It accepts any non-empty
/// organization/branch credentials and grants a seat locally, so the sign-in flow, tenancy and
/// scheduler can be exercised end-to-end offline. Sync calls are no-ops. The real backend is the
/// <c>HttpCloudGateway</c> (Phase 3), selected automatically when an API base URL is configured.
/// </summary>
public sealed class StubCloudGateway : ICloudGateway
{
    public bool IsConfigured => false;

    public Task<SignInResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationCode) || string.IsNullOrWhiteSpace(request.BranchCode))
            return Task.FromResult(SignInResult.Fail("Enter your organization and branch codes."));

        var orgId = Slug(request.OrganizationCode);
        var session = new Core.Models.Tenancy.AuthSession
        {
            Token = Guid.NewGuid().ToString("N"),
            OrganizationId = orgId,
            OrganizationName = request.OrganizationCode.Trim(),
            BranchId = $"{orgId}:{Slug(request.BranchCode)}",
            BranchName = request.BranchCode.Trim(),
            DeviceId = request.DeviceId,
            SeatCount = 5,
            SeatsUsed = 1,
            LastValidatedUtc = DateTime.UtcNow,
        };

        Log.Information("StubCloudGateway: granted local seat for org '{Org}' branch '{Branch}'",
            session.OrganizationName, session.BranchName);
        return Task.FromResult(SignInResult.Ok(session));
    }

    public Task<SignInResult> ValidateAsync(Core.Models.Tenancy.AuthSession session, CancellationToken cancellationToken = default)
    {
        session.LastValidatedUtc = DateTime.UtcNow;
        return Task.FromResult(SignInResult.Ok(session));
    }

    public Task SignOutAsync(Core.Models.Tenancy.AuthSession session, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<SongSyncBatch> PullSongsAsync(string organizationId, string? sinceCursor, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SongSyncBatch(Array.Empty<Song>(), sinceCursor));

    public Task PushSongsAsync(string organizationId, IReadOnlyList<Song> songs, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private static string Slug(string s) =>
        new string(s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
