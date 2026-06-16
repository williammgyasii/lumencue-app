namespace ChurchProjection.Core.Services;

/// <summary>
/// Fetches a short-lived speech-to-text access token from the cloud API. Returns null when the
/// token cannot be obtained (offline, signed out), which signals callers to fall back to offline STT.
/// </summary>
public interface ISttTokenProvider
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}
