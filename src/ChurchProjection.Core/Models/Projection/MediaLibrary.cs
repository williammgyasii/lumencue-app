namespace ChurchProjection.Core.Models.Projection;

/// <summary>
/// Pure helpers over a media library list. Kept free of UI/VLC dependencies so the dedup rule can be
/// unit-tested and reused by the service.
/// </summary>
public static class MediaLibrary
{
    /// <summary>
    /// Finds an existing item that refers to the same file as <paramref name="path"/>, comparing
    /// normalized full paths case-insensitively (matching Windows file systems). Returns null when the
    /// file is not already in the library — used to avoid adding duplicates.
    /// </summary>
    public static AnnouncementMedia? FindByPath(IEnumerable<AnnouncementMedia> items, string path)
    {
        if (items is null || string.IsNullOrWhiteSpace(path)) return null;

        var target = Normalize(path);
        return items.FirstOrDefault(i => Normalize(i.Path) == target);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant(); }
        catch { return path.Trim().ToLowerInvariant(); }
    }
}
