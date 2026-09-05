namespace ChurchProjection.Core.Services;

/// <summary>
/// Prefetch may download every picker Bible. Topical MiniLM indexing stays on the selected one
/// so a booth PC is not asked to embed thirteen full texts at once.
/// </summary>
public static class BiblePrefetchIndex
{
    public static bool ShouldIndex(string? cached, string? selected)
    {
        if (string.IsNullOrWhiteSpace(cached) || string.IsNullOrWhiteSpace(selected))
            return false;
        return string.Equals(cached.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
