namespace ChurchProjection.Core.Services;

public static class SongTitle
{
    public static string ToSentenceCase(string? title)
    {
        var trimmed = (title ?? "").Trim();
        if (trimmed.Length == 0) return "";
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }
}

public static class SongEditorRules
{
    public static bool CanSave(string? title, string? artist) =>
        !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist);

    public static bool SameBreakdown(
        IReadOnlyList<(string Type, string Text)> left,
        IReadOnlyList<(string Type, string Text)> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Type.Equals(right[i].Type, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(left[i].Text, right[i].Text, StringComparison.Ordinal)) return false;
        }
        return true;
    }
}

/// <summary>
/// Combo labels for the lines-per-slide control. "Auto" is 0 (theme fit).
/// String choices avoid Avalonia ComboBox failing to select boxed ints.
/// </summary>
public static class SongLinesPerSlide
{
    public static IReadOnlyList<string> Choices { get; } = ["Auto", "1", "2", "3", "4", "5", "6", "8"];

    public static string ToChoice(int lines) => lines <= 0 ? "Auto" : lines.ToString();

    public static int FromChoice(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice) || choice.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return 0;
        return int.TryParse(choice, out var n) && n > 0 ? n : 0;
    }

    /// <summary>Same paging Now Singing uses: 0 = one card for the whole section.</summary>
    public static IReadOnlyList<string> SplitPages(string? body, int linesPerSlide)
    {
        var normalized = (body ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length == 0) return [];
        if (linesPerSlide <= 0) return [normalized];

        var pages = new List<string>();
        foreach (var stanza in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = stanza.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            for (var i = 0; i < lines.Count; i += linesPerSlide)
                pages.Add(string.Join("\n", lines.Skip(i).Take(linesPerSlide)));
        }
        return pages;
    }
}
