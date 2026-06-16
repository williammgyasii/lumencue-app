using System.Net.Http.Json;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Audio;

/// <summary>
/// Requests a short-lived Deepgram JWT from the cloud API's POST /stt/token endpoint. The supplied
/// HttpClient must be preconfigured with the API base address and seat-token (Bearer) authentication.
///
/// The token is cached in memory until shortly before it expires, so the startup probe + connect, and
/// any quick reconnects (e.g. a brief Wi-Fi drop mid-service), reuse it with no extra network round
/// trip. Deepgram only needs the token to be valid at connect time; the socket then streams directly.
/// </summary>
public sealed class HttpSttTokenProvider : ISttTokenProvider
{
    // Refresh a little before the real expiry so a connect never races a just-expired token.
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _cachedUntil = DateTimeOffset.MinValue;

    public HttpSttTokenProvider(HttpClient http) => _http = http;

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: a still-valid cached token, no lock/network.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _cachedUntil)
            return _cachedToken;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock in case another caller just refreshed it.
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _cachedUntil)
                return _cachedToken;

            using var resp = await _http.PostAsync("stt/token", content: null, cancellationToken)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("STT token request failed: {Status}", resp.StatusCode);
                return null;
            }

            var body = await resp.Content.ReadFromJsonAsync<SttTokenResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (body is null || string.IsNullOrWhiteSpace(body.AccessToken))
                return null;

            var lifetime = TimeSpan.FromSeconds(Math.Max(body.ExpiresIn, 0));
            var usable = lifetime - ExpirySafetyMargin;
            if (usable > TimeSpan.Zero)
            {
                _cachedToken = body.AccessToken;
                _cachedUntil = DateTimeOffset.UtcNow + usable;
            }

            return body.AccessToken;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not obtain STT token (offline?)");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record SttTokenResponse(string AccessToken, double ExpiresIn);
}
