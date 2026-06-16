namespace ChurchProjection.UI.ViewModels;

/// <summary>
/// A selectable projector output target. Either a physical display (full screen) or a
/// windowed preview for single-monitor setups / testing.
/// </summary>
public sealed record DisplayOption(string Name, int X, int Y, int Width, int Height, bool IsWindowedPreview = false)
{
    /// <summary>Stable key used to persist/restore the chosen output across restarts.</summary>
    public string Key => IsWindowedPreview ? "windowed" : $"{X},{Y}";

    public override string ToString() => Name;
}
