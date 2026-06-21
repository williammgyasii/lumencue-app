namespace ChurchProjection.Core.Models.Projection;

/// <summary>Whether an announcement is a still graphic or a video clip (which may carry audio).</summary>
public enum AnnouncementMediaKind
{
    Image,
    Video,
}

/// <summary>
/// A piece of announcement media (a full-screen graphic, a lower-third graphic, or a video) the
/// operator can push live to the screens. Unlike a decorative live background, an announcement is the
/// primary content — videos play with sound, and the artwork itself decides whether it looks
/// full-screen or lower-third (the renderer just shows it on top, centred to the canvas).
/// </summary>
public sealed class AnnouncementMedia
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public AnnouncementMediaKind Kind { get; set; }

    /// <summary>The collection (folder) this asset belongs to, or null for "Uncategorized". Null on
    /// older libraries so existing media keeps working and simply shows as uncategorized.</summary>
    public string? CollectionId { get; set; }
}

/// <summary>
/// A single-level named folder for grouping media assets (e.g. "Easter graphics"). Purely
/// organizational — it has no effect on routing or playback.
/// </summary>
public sealed class MediaCollection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
}
