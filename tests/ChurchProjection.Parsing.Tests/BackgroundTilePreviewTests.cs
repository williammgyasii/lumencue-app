using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.ViewModels.Operator;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class BackgroundTilePreviewTests
{
    [Fact]
    public void RequestFor_Video_IsMutedStillAtThumbnailSize()
    {
        var item = new LiveBackground
        {
            Name = "loop",
            Path = "/tmp/clip.mp4",
            Kind = LiveBackgroundKind.Video,
        };

        var request = BackgroundTilePreview.RequestFor(item);

        Assert.NotNull(request);
        Assert.Equal("/tmp/clip.mp4", request.Path);
        Assert.False(request.Loop);
        Assert.False(request.Audio);
        Assert.Equal(BackgroundTilePreview.MaxWidth, request.MaxWidth);
        Assert.Equal(BackgroundTilePreview.MaxHeight, request.MaxHeight);
    }

    [Fact]
    public void RequestFor_Image_IsNull()
    {
        var item = new LiveBackground
        {
            Path = "/tmp/still.jpg",
            Kind = LiveBackgroundKind.Image,
        };

        Assert.Null(BackgroundTilePreview.RequestFor(item));
    }
}
