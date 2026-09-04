using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.ViewModels.Operator;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class MediaTilePreviewTests
{
    [Fact]
    public void RequestFor_Video_IsMutedStillAtTileSize()
    {
        var item = new AnnouncementMedia
        {
            Name = "bumper",
            Path = "/tmp/bumper.mp4",
            Kind = AnnouncementMediaKind.Video,
        };

        var request = MediaTilePreview.RequestFor(item);

        Assert.NotNull(request);
        Assert.Equal("/tmp/bumper.mp4", request.Path);
        Assert.False(request.Audio);
        Assert.Equal(MediaTilePreview.MaxWidth, request.MaxWidth);
        Assert.Equal(MediaTilePreview.MaxHeight, request.MaxHeight);
    }

    [Fact]
    public void RequestFor_Image_IsNull()
    {
        var item = new AnnouncementMedia
        {
            Path = "/tmp/slide.png",
            Kind = AnnouncementMediaKind.Image,
        };

        Assert.Null(MediaTilePreview.RequestFor(item));
    }
}
