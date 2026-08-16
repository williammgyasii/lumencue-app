using ChurchProjection.Infrastructure.Audio;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ElevenLabsScribeTests
{
    [Fact]
    public void Create_UsesScribeV2RealtimeAndVadCommit()
    {
        var options = ElevenLabsScribeOptions.Create(sampleRate: 48000);

        Assert.Equal("scribe_v2_realtime", options.ModelId);
        Assert.Equal("vad", options.CommitStrategy);
        Assert.Equal("en", options.LanguageCode);
        Assert.Equal("pcm_48000", options.AudioFormat);
        Assert.Equal(48000, options.SampleRate);
    }

    [Fact]
    public void Create_UnknownRateFallsBackTo16k()
    {
        var options = ElevenLabsScribeOptions.Create(sampleRate: 32000);

        Assert.Equal("pcm_16000", options.AudioFormat);
        Assert.Equal(16000, options.SampleRate);
    }

    [Fact]
    public void Create_TrimsKeytermsToScribeRealtimeLimits()
    {
        var options = ElevenLabsScribeOptions.Create(sampleRate: 16000);

        Assert.True(options.Keyterms.Count <= 50);
        Assert.All(options.Keyterms, term => Assert.True(term.Length <= 20));
        Assert.Contains("Genesis", options.Keyterms);
        Assert.Contains("Corinthians", options.Keyterms);
        Assert.Contains("chapter", options.Keyterms);
        Assert.Contains("verse", options.Keyterms);
        Assert.DoesNotContain(options.Keyterms, t => t.Contains(' '));
    }

    [Fact]
    public void BuildUri_IncludesModelFormatAndKeyterms()
    {
        var options = ElevenLabsScribeOptions.Create(sampleRate: 16000);
        var uri = options.BuildWebSocketUri();

        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("api.elevenlabs.io", uri.Host);
        Assert.Equal("/v1/speech-to-text/realtime", uri.AbsolutePath);
        Assert.Contains("model_id=scribe_v2_realtime", uri.Query);
        Assert.Contains("audio_format=pcm_16000", uri.Query);
        Assert.Contains("commit_strategy=vad", uri.Query);
        Assert.Contains("keyterms=Genesis", uri.Query);
        Assert.DoesNotContain("token=", uri.Query);
    }

    [Fact]
    public void BuildUri_WithSingleUseToken_PutsTokenInQuery()
    {
        var options = ElevenLabsScribeOptions.Create(sampleRate: 16000);
        var uri = options.BuildWebSocketUri("scribe-token-abc");

        Assert.Contains("token=scribe-token-abc", uri.Query);
        Assert.Contains("model_id=scribe_v2_realtime", uri.Query);
    }

    [Fact]
    public void ParseMintResponse_ReadsToken()
    {
        var token = ElevenLabsScribeToken.Parse("""{"token":"sut_live_abc"}""");

        Assert.Equal("sut_live_abc", token);
    }

    [Fact]
    public void ParseMintResponse_MissingToken_IsNull()
    {
        Assert.Null(ElevenLabsScribeToken.Parse("""{"error":"unauthorized"}"""));
        Assert.Null(ElevenLabsScribeToken.Parse(""));
    }

    [Fact]
    public void MintPath_IsRealtimeScribeSingleUse()
    {
        Assert.Equal("v1/single-use-token/realtime_scribe", ElevenLabsScribeToken.MintPath);
        Assert.Equal(900, ElevenLabsScribeToken.LifetimeSeconds);
    }

    [Fact]
    public void Parse_PartialTranscript_IsInterim()
    {
        var parsed = ElevenLabsScribeMessage.Parse(
            """{"message_type":"partial_transcript","text":"John three"}""");

        Assert.Equal(ElevenLabsScribeKind.Interim, parsed.Kind);
        Assert.Equal("John three", parsed.Text);
    }

    [Fact]
    public void Parse_CommittedTranscript_IsFinal()
    {
        var parsed = ElevenLabsScribeMessage.Parse(
            """{"message_type":"committed_transcript","text":"John 3 16"}""");

        Assert.Equal(ElevenLabsScribeKind.Final, parsed.Kind);
        Assert.Equal("John 3 16", parsed.Text);
    }

    [Fact]
    public void Parse_AuthError_IsError()
    {
        var parsed = ElevenLabsScribeMessage.Parse(
            """{"message_type":"auth_error","error":"invalid key"}""");

        Assert.Equal(ElevenLabsScribeKind.Error, parsed.Kind);
        Assert.Equal("invalid key", parsed.Error);
    }

    [Fact]
    public void Parse_SessionStarted_IsIgnored()
    {
        var parsed = ElevenLabsScribeMessage.Parse(
            """{"message_type":"session_started","session_id":"abc"}""");

        Assert.Equal(ElevenLabsScribeKind.Ignored, parsed.Kind);
    }

    [Fact]
    public void EncodeAudioChunk_IsBase64PcmWithSampleRate()
    {
        var json = ElevenLabsScribeMessage.EncodeAudioChunk([0x01, 0x02], sampleRate: 16000);

        Assert.Contains("\"message_type\":\"input_audio_chunk\"", json);
        Assert.Contains("\"audio_base_64\":\"AQI=\"", json);
        Assert.Contains("\"commit\":false", json);
        Assert.Contains("\"sample_rate\":16000", json);
    }
}
