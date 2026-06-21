namespace ChurchProjection.Core.Services;

/// <summary>
/// Copies imported design images into an app-managed folder so a theme that references them stays
/// self-contained — the theme keeps working even if the operator moves or deletes the original file
/// they picked (e.g. a download). Returns the path to the stored copy for the theme to point at.
/// </summary>
public interface IThemeAssetStore
{
    /// <summary>Copies <paramref name="sourcePath"/> into the asset folder and returns the absolute
    /// path of the stored copy. The original file is left untouched.</summary>
    string Save(string sourcePath);
}
