using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.ViewModels.Operator;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class AnnouncementPlaybackTests
{
    [Fact]
    public void RequestFor_Video_PlaysOnceWithAudio()
    {
        var item = new AnnouncementMedia
        {
            Name = "bumper",
            Path = "/tmp/bumper.mp4",
            Kind = AnnouncementMediaKind.Video,
        };

        var request = AnnouncementPlayback.RequestFor(item, "device-1");

        Assert.NotNull(request);
        Assert.Equal("/tmp/bumper.mp4", request.Path);
        Assert.False(request.Loop);
        Assert.True(request.Audio);
        Assert.Equal("device-1", request.AudioDeviceId);
    }

    [Fact]
    public void RequestFor_Image_IsNull()
    {
        var item = new AnnouncementMedia
        {
            Path = "/tmp/slide.png",
            Kind = AnnouncementMediaKind.Image,
        };

        Assert.Null(AnnouncementPlayback.RequestFor(item, audioDeviceId: null));
    }

    [Fact]
    public void RequestFor_EmptyPath_IsNull()
    {
        var item = new AnnouncementMedia
        {
            Path = "  ",
            Kind = AnnouncementMediaKind.Video,
        };

        Assert.Null(AnnouncementPlayback.RequestFor(item, audioDeviceId: null));
    }
}
