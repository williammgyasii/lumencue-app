using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace ChurchProjection.UI.Services.Video;

/// <summary>
/// Picks the OS decoder and starts a frame-pumping player.
/// Mac uses AVFoundation; Windows (and other OS) uses bundled LibVLC.
/// </summary>
public static class VideoFramePlayerFactory
{
    public static VideoFrameEngine ResolveEngine() =>
        ResolveEngineFor(OperatingSystem.IsMacOS());

    public static VideoFrameEngine ResolveEngineFor(bool isMacOs) =>
        isMacOs ? VideoFrameEngine.AvFoundation : VideoFrameEngine.LibVlc;

    public static IVideoFramePlayer Start(VideoPlayRequest request, Action<Bitmap> onFrame)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onFrame);

        if (ResolveEngine() == VideoFrameEngine.AvFoundation)
            return new AvFoundationVideoFramePlayer(request, onFrame);

        if (request.Audio)
            return new VlcMediaPlayer(request.Path, request.Loop, request.AudioDeviceId, onFrame);

        return new VlcBackgroundPlayer(request.Path, onFrame);
    }

    public static IReadOnlyList<AudioOutputOption> EnumerateAudioDevices()
    {
        if (ResolveEngine() == VideoFrameEngine.AvFoundation)
            return [new AudioOutputOption(string.Empty, "System default")];

        return VlcMediaPlayer.EnumerateAudioDevices();
    }
}
