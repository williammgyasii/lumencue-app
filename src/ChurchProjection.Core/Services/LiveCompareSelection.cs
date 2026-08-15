namespace ChurchProjection.Core.Services;

/// <summary>
/// Now Live can show two other translations of the verse on screen.
/// The operator picks them with checkboxes; this keeps the cap and the live-skip rule.
/// </summary>
public static class LiveCompareSelection
{
    public const int MaxSlots = 2;

    public static IReadOnlyList<string> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSlots)
            .ToList();
    }

    public static string Format(IEnumerable<string> codes)
        => string.Join(",", codes.Where(c => !string.IsNullOrWhiteSpace(c)).Take(MaxSlots));

    /// <summary>
    /// The two cards to show. Drops the translation that just went live, then fills the empty
    /// slot from <paramref name="available"/> so both boxes stay occupied.
    /// </summary>
    public static IReadOnlyList<string> ForDisplay(
        IEnumerable<string> chosen,
        string? liveTranslation,
        IEnumerable<string>? available = null)
    {
        var shown = new List<string>();
        foreach (var code in chosen)
            TryAdd(shown, code, liveTranslation);

        if (available is not null)
        {
            foreach (var code in available)
            {
                if (shown.Count >= MaxSlots) break;
                TryAdd(shown, code, liveTranslation);
            }
        }

        return shown;
    }

    private static void TryAdd(List<string> shown, string? code, string? liveTranslation)
    {
        if (shown.Count >= MaxSlots || string.IsNullOrWhiteSpace(code)) return;
        if (string.Equals(code, liveTranslation, StringComparison.OrdinalIgnoreCase)) return;
        if (shown.Any(s => string.Equals(s, code, StringComparison.OrdinalIgnoreCase))) return;
        shown.Add(code);
    }

    public static bool Toggle(IList<string> chosen, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        for (var i = 0; i < chosen.Count; i++)
        {
            if (!string.Equals(chosen[i], code, StringComparison.OrdinalIgnoreCase))
                continue;
            chosen.RemoveAt(i);
            return true;
        }

        if (chosen.Count >= MaxSlots) return false;
        chosen.Add(code);
        return true;
    }
}
