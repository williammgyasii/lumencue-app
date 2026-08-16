namespace ChurchProjection.Infrastructure.Audio;

/// <summary>
/// Streaming listen options for ElevenLabs Scribe v2 Realtime.
/// Keyterms are capped at Scribe realtime limits (50 terms, 20 chars, no spaces)
/// so the WebSocket query stays valid. Production auth is a single-use
/// <c>token</c> query param; a long-lived <c>xi-api-key</c> is local-dev only.
/// </summary>
public sealed record ElevenLabsScribeOptions(
    string ModelId,
    string CommitStrategy,
    string LanguageCode,
    string AudioFormat,
    int SampleRate,
    IReadOnlyList<string> Keyterms)
{
    public const string Model = "scribe_v2_realtime";
    public const int MaxKeyterms = 50;
    public const int MaxKeytermChars = 20;

    private static readonly int[] SupportedRates = [8000, 16000, 22050, 24000, 44100, 48000];

    // Curated for spoken scripture matching. Order is priority: cue words and
    // commonly cited books first so the 50-term cap keeps the ones that matter.
    private static readonly string[] BibleKeyterms =
    [
        "First", "Second", "chapter", "verse",
        "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy",
        "Joshua", "Judges", "Ruth", "Samuel", "Kings", "Chronicles",
        "Ezra", "Nehemiah", "Esther", "Job", "Psalms", "Psalm",
        "Proverbs", "Solomon", "Isaiah", "Jeremiah", "Ezekiel", "Daniel",
        "Hosea", "Matthew", "Mark", "Luke", "John", "Acts", "Romans",
        "Corinthians", "Galatians", "Ephesians", "Philippians",
        "Colossians", "Thessalonians", "Timothy", "Titus", "Philemon",
        "Hebrews", "James", "Peter", "Jude", "Revelation",
        "Jesus", "Christ",
    ];

    public static ElevenLabsScribeOptions Create(int sampleRate)
    {
        var rate = SupportedRates.Contains(sampleRate) ? sampleRate : 16000;
        var keyterms = BibleKeyterms
            .Where(t => t.Length <= MaxKeytermChars && !t.Contains(' '))
            .Take(MaxKeyterms)
            .ToArray();

        return new ElevenLabsScribeOptions(
            ModelId: Model,
            CommitStrategy: "vad",
            LanguageCode: "en",
            AudioFormat: $"pcm_{rate}",
            SampleRate: rate,
            Keyterms: keyterms);
    }

    public Uri BuildWebSocketUri(string? singleUseToken = null)
    {
        var query = new List<string>
        {
            $"model_id={Uri.EscapeDataString(ModelId)}",
            $"language_code={Uri.EscapeDataString(LanguageCode)}",
            $"commit_strategy={Uri.EscapeDataString(CommitStrategy)}",
            $"audio_format={Uri.EscapeDataString(AudioFormat)}",
            "filter_background_audio=true",
        };
        if (!string.IsNullOrWhiteSpace(singleUseToken))
            query.Add($"token={Uri.EscapeDataString(singleUseToken)}");
        foreach (var term in Keyterms)
            query.Add($"keyterms={Uri.EscapeDataString(term)}");

        return new Uri($"wss://api.elevenlabs.io/v1/speech-to-text/realtime?{string.Join("&", query)}");
    }
}
