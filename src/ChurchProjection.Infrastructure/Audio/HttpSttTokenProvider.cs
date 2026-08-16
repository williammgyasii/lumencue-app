using System.Net.Http.Json;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Audio;

/// <summary>
/// Requests a short-lived STT token from POST /stt/token. The supplied HttpClient must
/// already carry the API base address and seat-token (Bearer) authentication.
///
/// Tokens are not cached: ElevenLabs Scribe single-use tokens are consumed on the first
/// WebSocket connect, so a reconnect must mint a new one (and that mint is what
/// <c>stt_usage</c> records).
/// </summary>
public sealed class HttpSttTokenProvider : ISttTokenProvider
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HttpSttTokenProvider(HttpClient http) => _http = http;

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var resp = await _http.PostAsync("stt/token", content: null, cancellationToken)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("STT token request failed: {Status}", resp.StatusCode);
                return null;
            }

            var body = await resp.Content.ReadFromJsonAsync<SttTokenResponse>(cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body?.AccessToken) ? null : body.AccessToken;
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
