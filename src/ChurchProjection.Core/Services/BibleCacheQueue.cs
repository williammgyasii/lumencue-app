namespace ChurchProjection.Core.Services;

/// <summary>Download order for Bibles on the operator picker. Selected first; helloao extras stay out.</summary>
public static class BibleCacheQueue
{
    public static IReadOnlyList<string> Order(string? selected, IEnumerable<string> offered)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new List<string>();

        if (!string.IsNullOrWhiteSpace(selected) && seen.Add(selected.Trim()))
            queue.Add(selected.Trim());

        foreach (var code in offered)
        {
            if (string.IsNullOrWhiteSpace(code))
                continue;
            var trimmed = code.Trim();
            if (seen.Add(trimmed))
                queue.Add(trimmed);
        }

        return queue;
    }
}
