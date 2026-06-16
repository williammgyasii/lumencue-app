namespace ChurchProjection.Core.Models.Projection;

/// <summary>Whether a live background is a still image or a looping motion clip.</summary>
public enum LiveBackgroundKind
{
    Image,
    Video,
}

/// <summary>
/// A swappable background media item (still image or motion loop) that the operator can flip live
/// underneath the text. It is intentionally independent of the <see cref="Theme.Theme"/>: the theme
/// owns the text layout/look, while the live background is the media layer painted behind it. Outputs
/// whose theme uses a key colour (green/black for an ATEM keyer) ignore the live background so the
/// chroma feed stays clean.
/// </summary>
public sealed class LiveBackground
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public LiveBackgroundKind Kind { get; set; }
}
