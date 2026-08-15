namespace ChurchProjection.Core.Services;

/// <summary>
/// Replaces or inserts one planned note page, then joins pages back into a single body.
/// When <paramref name="linesPerSlide"/> is set, pages are joined with single newlines
/// (the operator packed N lines per slide). Otherwise blank lines separate slides.
/// </summary>
public static class NoteSlideEdit
{
    public static IReadOnlyList<string> Replace(IReadOnlyList<string> pages, int index, string? text)
    {
        var next = pages.ToList();
        if (next.Count == 0)
            return [text ?? ""];
        var i = Math.Clamp(index, 0, next.Count - 1);
        next[i] = text ?? "";
        return next;
    }

    public static IReadOnlyList<string> InsertAfter(IReadOnlyList<string> pages, int index, string? text)
    {
        var next = pages.ToList();
        var i = next.Count == 0 ? 0 : Math.Clamp(index, 0, next.Count - 1) + 1;
        next.Insert(i, text ?? "");
        return next;
    }

    public static string Join(IReadOnlyList<string> pages, int linesPerSlide) =>
        string.Join(linesPerSlide > 0 ? "\n" : "\n\n",
            pages.Select(p => (p ?? "").Trim()).Where(p => p.Length > 0));
}
