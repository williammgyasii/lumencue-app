using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Builds one bookmark for a shift-selected verse span (e.g. Genesis 1:3-5) so Send-to-Live
/// projects the combined text instead of a single card.
/// </summary>
public static class ScriptureRangeBookmark
{
    public static SuggestionItem FromItems(IReadOnlyList<ContentItem> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("Need at least one verse.", nameof(items));

        var first = items[0].Source as ScripturePassage;
        var last = items[^1].Source as ScripturePassage;
        var book = first?.Book ?? "Scripture";
        var chapter = first?.Chapter ?? 0;
        var start = first?.VerseStart ?? 1;
        var end = last?.VerseStart ?? start;

        var title = end != start
            ? $"{book} {chapter}:{start}-{end}"
            : $"{book} {chapter}:{start}";
        var contentId = end != start
            ? $"scripture:{book}:{chapter}:{start}-{end}"
            : $"scripture:{book}:{chapter}:{start}";

        var body = items.Count == 1
            ? items[0].Body
            : string.Join(" ", items.Select(i => i.Body).Where(b => !string.IsNullOrWhiteSpace(b)));

        var tag = items[0].Tag;
        var footer = string.IsNullOrWhiteSpace(tag) ? title : $"{title} ({tag})";

        return new SuggestionItem
        {
            ContentId = contentId,
            Title = title,
            Body = body,
            Footer = footer,
            MatchType = "scripture_reference",
            IsBookmarked = true,
        };
    }
}
