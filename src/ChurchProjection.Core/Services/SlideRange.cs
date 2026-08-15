using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Inclusive index span for shift-click slide selection. Click direction does not matter:
/// verse 8 then Shift+verse 3 is the same range as 3 then 8.
/// </summary>
public static class SlideRange
{
    public static (int Start, int End) Inclusive(int a, int b)
        => a <= b ? (a, b) : (b, a);

    public static void Apply(IList<ContentItem> items, int fromIndex, int toIndex)
    {
        var (start, end) = Inclusive(fromIndex, toIndex);
        for (var i = 0; i < items.Count; i++)
            items[i].IsRangeSelected = i >= start && i <= end;
    }
}
