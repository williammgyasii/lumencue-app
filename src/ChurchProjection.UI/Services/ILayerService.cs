using System;
using ChurchProjection.Core.Models.Projection;

namespace ChurchProjection.UI.Services;

/// <summary>
/// An immutable snapshot of one channel's full layer stack: the enable/opacity of each layer plus the
/// content owned directly by this service (the Overlay logo and the Alert banner). Background / Slide /
/// Media content come from their own services; this only gates how they composite.
/// </summary>
public sealed record LayerSnapshot(
    bool BackgroundEnabled, double BackgroundOpacity,
    bool SlideEnabled, double SlideOpacity,
    bool MediaEnabled, double MediaOpacity,
    bool OverlayEnabled, double OverlayOpacity,
    bool AlertEnabled, double AlertOpacity,
    string? OverlayImagePath, OverlayAnchor OverlayAnchor, double OverlayScale,
    string? AlertText)
{
    /// <summary>Every layer enabled, fully opaque, no overlay/alert content — the default for a fresh channel.</summary>
    public static LayerSnapshot Default { get; } = new(
        true, 1, true, 1, true, 1, true, 1, true, 1,
        null, OverlayAnchor.TopRight, 0.18, null);
}

/// <summary>
/// Owns the per-channel compositing state for every projector output: which layers are on, their
/// opacity, the persistent overlay logo, and the alert banner. State is kept PER CHANNEL so a full
/// LED feed and an ATEM lower-third can stack differently at the same time.
/// </summary>
public interface ILayerService
{
    /// <summary>The current snapshot for a channel (never null; defaults to all-on if untouched).</summary>
    LayerSnapshot Snapshot(string channelId);

    /// <summary>A channel's snapshot stream — emits the current value immediately, then on every change.</summary>
    IObservable<LayerSnapshot> SnapshotFor(string channelId);

    /// <summary>Toggle a layer on/off for a channel.</summary>
    void SetEnabled(string channelId, ProjectionLayerKind kind, bool enabled);

    /// <summary>Set a layer's opacity (0..1) for a channel.</summary>
    void SetOpacity(string channelId, ProjectionLayerKind kind, double opacity);

    /// <summary>Set (or clear, with a null path) the persistent overlay logo for a channel.</summary>
    void SetOverlay(string channelId, string? imagePath, OverlayAnchor anchor, double scale);

    /// <summary>Set (or clear, with null/empty) the alert banner text for a channel.</summary>
    void SetAlert(string channelId, string? text);
}
