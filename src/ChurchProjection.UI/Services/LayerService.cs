using System;
using System.Collections.Concurrent;
using System.Reactive.Subjects;
using ChurchProjection.Core.Models.Projection;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Default <see cref="ILayerService"/>. Holds a mutable layer stack per channel and republishes an
/// immutable <see cref="LayerSnapshot"/> on every change, which both the matching projector output and
/// the operator's Layers strip subscribe to. State is in-memory and intentionally NOT persisted, so the
/// app always launches with a clean, fully-lit stack (no surprise logos/alerts/dimming on startup).
/// </summary>
public sealed class LayerService : ILayerService
{
    private readonly ConcurrentDictionary<string, ChannelLayers> _channels = new();

    public LayerSnapshot Snapshot(string channelId) => Get(channelId).Subject.Value;

    public IObservable<LayerSnapshot> SnapshotFor(string channelId) => Get(channelId).Subject;

    public void SetEnabled(string channelId, ProjectionLayerKind kind, bool enabled)
    {
        var c = Get(channelId);
        var s = c.Subject.Value;
        c.Publish(kind switch
        {
            ProjectionLayerKind.Background => s with { BackgroundEnabled = enabled },
            ProjectionLayerKind.Slide => s with { SlideEnabled = enabled },
            ProjectionLayerKind.Media => s with { MediaEnabled = enabled },
            ProjectionLayerKind.Overlay => s with { OverlayEnabled = enabled },
            ProjectionLayerKind.Alert => s with { AlertEnabled = enabled },
            _ => s,
        });
    }

    public void SetOpacity(string channelId, ProjectionLayerKind kind, double opacity)
    {
        var o = Math.Clamp(opacity, 0, 1);
        var c = Get(channelId);
        var s = c.Subject.Value;
        c.Publish(kind switch
        {
            ProjectionLayerKind.Background => s with { BackgroundOpacity = o },
            ProjectionLayerKind.Slide => s with { SlideOpacity = o },
            ProjectionLayerKind.Media => s with { MediaOpacity = o },
            ProjectionLayerKind.Overlay => s with { OverlayOpacity = o },
            ProjectionLayerKind.Alert => s with { AlertOpacity = o },
            _ => s,
        });
    }

    public void SetOverlay(string channelId, string? imagePath, OverlayAnchor anchor, double scale)
    {
        var c = Get(channelId);
        var s = c.Subject.Value;
        c.Publish(s with
        {
            OverlayImagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath,
            OverlayAnchor = anchor,
            OverlayScale = Math.Clamp(scale, 0.02, 1),
        });
    }

    public void SetAlert(string channelId, string? text)
    {
        var c = Get(channelId);
        var s = c.Subject.Value;
        c.Publish(s with { AlertText = string.IsNullOrWhiteSpace(text) ? null : text });
    }

    private ChannelLayers Get(string? key) =>
        _channels.GetOrAdd(string.IsNullOrEmpty(key) ? MediaTarget.AllScreens : key,
            _ => new ChannelLayers());

    private sealed class ChannelLayers
    {
        public BehaviorSubject<LayerSnapshot> Subject { get; } = new(LayerSnapshot.Default);
        public void Publish(LayerSnapshot s) => Subject.OnNext(s);
    }
}
