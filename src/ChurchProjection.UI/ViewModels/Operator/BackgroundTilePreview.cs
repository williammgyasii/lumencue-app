using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.Services.Video;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>Muted first-frame request used to fill a background palette tile.</summary>
public static class BackgroundTilePreview
{
    public const int MaxWidth = 240;
    public const int MaxHeight = 136;

    public static VideoPlayRequest? RequestFor(LiveBackground item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind != LiveBackgroundKind.Video || string.IsNullOrWhiteSpace(item.Path))
            return null;

        return new VideoPlayRequest(item.Path, Loop: false, Audio: false, MaxWidth: MaxWidth, MaxHeight: MaxHeight);
    }
}
