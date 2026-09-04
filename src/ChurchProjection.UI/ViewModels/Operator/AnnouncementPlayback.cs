using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.Services.Video;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>Live play request for a Media-tab clip: once through with audio, never a silent loop.</summary>
public static class AnnouncementPlayback
{
    public static VideoPlayRequest? RequestFor(AnnouncementMedia item, string? audioDeviceId)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind != AnnouncementMediaKind.Video || string.IsNullOrWhiteSpace(item.Path))
            return null;

        return new VideoPlayRequest(item.Path, Loop: false, Audio: true, AudioDeviceId: audioDeviceId);
    }
}
