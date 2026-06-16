namespace ChurchProjection.Core.Models.Projection;

/// <summary>
/// The fixed compositing stack every projector output renders, bottom → top. Each layer is toggled
/// and faded independently per channel:
///   Background → the theme colour / image / live motion loop.
///   Slide      → the themed text content (scripture, lyrics, announcement text) + shapes.
///   Media      → full-screen / lower-third graphics and videos (with sound).
///   Overlay    → a persistent logo / watermark that survives slide changes.
///   Alert      → a banner message punched over everything (e.g. "Children's church dismissed").
/// </summary>
public enum ProjectionLayerKind
{
    Background,
    Slide,
    Media,
    Overlay,
    Alert,
}

/// <summary>Where a persistent overlay (logo / watermark) is anchored on the 16:9 canvas.</summary>
public enum OverlayAnchor
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center,
}
