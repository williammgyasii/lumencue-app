using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SttOperatorCopyTests
{
    [Fact]
    public void Engine_label_is_transcription_agent()
    {
        Assert.Equal("Transcription agent", SttOperatorCopy.EngineLabel);
        Assert.DoesNotContain("ElevenLabs", SttOperatorCopy.EngineLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Scribe", SttOperatorCopy.EngineLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Deepgram", SttOperatorCopy.EngineLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Connecting_names_the_agent_not_the_vendor()
    {
        Assert.Equal("Connecting to transcription agent...", SttOperatorCopy.Connecting);
        Assert.DoesNotContain("ElevenLabs", SttOperatorCopy.Connecting, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Deepgram", SttOperatorCopy.Connecting, StringComparison.OrdinalIgnoreCase);
    }
}
