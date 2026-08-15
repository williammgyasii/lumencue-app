namespace ChurchProjection.Core.Services;

/// <summary>
/// One persistent Sunday setlist: add songs before service, then step through them.
/// Titles are compared case-insensitively; duplicates are ignored.
/// </summary>
public static class SundaySetlist
{
    public const string StorageName = "Sunday Playlist";

    public static bool TryAdd(IList<string> titles, string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var trimmed = title.Trim();
        if (titles.Any(t => t.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return false;
        titles.Add(trimmed);
        return true;
    }

    public static bool TryRemove(IList<string> titles, string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        for (var i = 0; i < titles.Count; i++)
        {
            if (!titles[i].Equals(title.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            titles.RemoveAt(i);
            return true;
        }
        return false;
    }

    /// <summary>The song after <paramref name="currentTitle"/>, or the first song if none is current.</summary>
    public static string? NextTitle(IReadOnlyList<string> titles, string? currentTitle)
    {
        if (titles.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(currentTitle)) return titles[0];

        for (var i = 0; i < titles.Count; i++)
        {
            if (!titles[i].Equals(currentTitle.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            return i + 1 < titles.Count ? titles[i + 1] : null;
        }

        return titles[0];
    }
}
