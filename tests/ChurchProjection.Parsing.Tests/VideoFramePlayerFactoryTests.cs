using ChurchProjection.UI.Services.Video;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class VideoFramePlayerFactoryTests
{
    [Fact]
    public void ResolveEngine_UsesAvFoundationOnMac_AndLibVlcElsewhere()
    {
        Assert.Equal(VideoFrameEngine.AvFoundation, VideoFramePlayerFactory.ResolveEngineFor(isMacOs: true));
        Assert.Equal(VideoFrameEngine.LibVlc, VideoFramePlayerFactory.ResolveEngineFor(isMacOs: false));
    }

    [Fact]
    public void CurrentOs_MatchesThisMachine()
    {
        var engine = VideoFramePlayerFactory.ResolveEngine();
        Assert.Equal(
            OperatingSystem.IsMacOS() ? VideoFrameEngine.AvFoundation : VideoFrameEngine.LibVlc,
            engine);
    }

    [Fact]
    public void VideoPlayRequest_PathOnly_DoesNotLoop()
    {
        var request = new VideoPlayRequest("/tmp/clip.mp4");

        Assert.False(request.Loop);
    }

    [Fact]
    public void Start_MissingFile_IsNotRunning()
    {
        using var player = VideoFramePlayerFactory.Start(
            new VideoPlayRequest("/tmp/lumencue-missing-clip.mp4"),
            _ => { });
        Assert.False(player.IsRunning);
    }
}
