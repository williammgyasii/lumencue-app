using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Stores imported design images under <c>{dataDirectory}/theme-assets</c> with a generated unique
/// name, so an imported theme references a stable, app-owned copy rather than wherever the operator
/// happened to pick the file from.
/// </summary>
public sealed class ThemeAssetStore : IThemeAssetStore
{
    private readonly string _root;

    public ThemeAssetStore(string dataDirectory)
        => _root = Path.Combine(dataDirectory, "theme-assets");

    public string Save(string sourcePath)
    {
        Directory.CreateDirectory(_root);
        var ext = Path.GetExtension(sourcePath);
        var dest = Path.Combine(_root, $"{Guid.NewGuid():N}{ext}");
        File.Copy(sourcePath, dest, overwrite: false);
        return dest;
    }
}
