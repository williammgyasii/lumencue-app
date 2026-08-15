using ChurchProjection.Infrastructure.Audio;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class DeepgramListenLatencyTests
{
    [Fact]
    public void LiveSchema_UsesFastEndpointingForLiveCaptions()
    {
        var schema = DeepgramListenOptions.Create(sampleRate: 48000);

        Assert.Equal("100", schema.EndPointing);
        Assert.Null(schema.UtteranceEnd);
        Assert.NotEqual(true, schema.VadEvents);
    }

    [Fact]
    public void LiveSchema_KeepsInterimResultsAndNoDelay()
    {
        var schema = DeepgramListenOptions.Create(sampleRate: 16000);

        Assert.Equal(true, schema.InterimResults);
        Assert.Equal(true, schema.NoDelay);
        Assert.Equal("nova-3", schema.Model);
        Assert.Equal(16000, schema.SampleRate);
        Assert.Equal("linear16", schema.Encoding);
        Assert.Equal(1, schema.Channels);
    }

    [Fact]
    public void WasapiCapture_UsesALowLatencyBuffer()
    {
        Assert.Equal(40, MicrophoneCaptureFactory.WasapiBufferMilliseconds);
    }
}
