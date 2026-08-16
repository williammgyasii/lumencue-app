using System.Text.Json;

namespace ChurchProjection.Infrastructure.Audio;

/// <summary>
/// Single-use Scribe token from POST /v1/single-use-token/realtime_scribe.
/// Consumed on the first WebSocket connect; expires after <see cref="LifetimeSeconds"/>.
/// </summary>
public static class ElevenLabsScribeToken
{
    public const string MintPath = "v1/single-use-token/realtime_scribe";
    public const int LifetimeSeconds = 900;

    public static string? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("token", out var tokenEl))
                return null;
            var token = tokenEl.GetString();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
