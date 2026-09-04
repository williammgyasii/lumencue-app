using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.Services.Video;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>Muted first-frame request used to fill a Media-bin video thumbnail.</summary>
public static class MediaTilePreview
{
    public const int MaxWidth = 320;
    public const int MaxHeight = 180;

    public static VideoPlayRequest? RequestFor(AnnouncementMedia item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind != AnnouncementMediaKind.Video || string.IsNullOrWhiteSpace(item.Path))
            return null;

        return new VideoPlayRequest(item.Path, Loop: false, Audio: false, MaxWidth: MaxWidth, MaxHeight: MaxHeight);
    }
}
