using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchProjection.Infrastructure.Audio;

public enum ElevenLabsScribeKind
{
    Ignored,
    Interim,
    Final,
    Error,
}

public readonly record struct ElevenLabsScribeMessage(
    ElevenLabsScribeKind Kind,
    string? Text,
    string? Error)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ElevenLabsScribeMessage Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("message_type", out var typeEl) ? typeEl.GetString() : null;
        var text = root.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
        var error = root.TryGetProperty("error", out var errorEl) ? errorEl.GetString() : null;

        return type switch
        {
            "partial_transcript" => new(ElevenLabsScribeKind.Interim, text, null),
            "committed_transcript" or "committed_transcript_with_timestamps"
                or "final_transcript" or "final_transcript_with_timestamps"
                => new(ElevenLabsScribeKind.Final, text, null),
            "auth_error" or "error" or "quota_exceeded" or "rate_limited"
                or "input_error" or "invalid_request" or "transcriber_error"
                or "session_time_limit_exceeded" or "resource_exhausted"
                => new(ElevenLabsScribeKind.Error, null, error),
            _ => new(ElevenLabsScribeKind.Ignored, null, null),
        };
    }

    public static string EncodeAudioChunk(byte[] pcm, int sampleRate)
    {
        var payload = new AudioChunkDto
        {
            MessageType = "input_audio_chunk",
            AudioBase64 = Convert.ToBase64String(pcm),
            Commit = false,
            SampleRate = sampleRate,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed class AudioChunkDto
    {
        [JsonPropertyName("message_type")]
        public string MessageType { get; set; } = "";

        [JsonPropertyName("audio_base_64")]
        public string AudioBase64 { get; set; } = "";

        [JsonPropertyName("commit")]
        public bool Commit { get; set; }

        [JsonPropertyName("sample_rate")]
        public int SampleRate { get; set; }
    }
}
