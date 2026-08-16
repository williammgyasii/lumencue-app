namespace ChurchProjection.Core.Services;

/// <summary>
/// Fetches a short-lived speech-to-text access token from the cloud API. Returns null when the
/// token cannot be obtained (offline, signed out). Callers must request a fresh token on every
/// (re)connect — ElevenLabs Scribe tokens are single-use.
/// </summary>
public interface ISttTokenProvider
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}
