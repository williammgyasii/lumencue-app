using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Models.Slides;

/// <summary>
/// Single source of truth for turning library/suggestion/queue items into projectable
/// <see cref="Slide"/>s, replacing the mapping logic that was duplicated across view models.
/// </summary>
public static class SlideMapper
{
    public static SlideType ToSlideType(this ContentItemType type) => type switch
    {
        ContentItemType.Scripture => SlideType.Scripture,
        ContentItemType.Song => SlideType.Lyric,
        _ => SlideType.Announcement,
    };

    public static Slide ToSlide(this ContentItem item) => new()
    {
        Type = item.Type.ToSlideType(),
        Title = item.Title,
        Body = item.Body,
        Footer = item.Footer,
    };

    public static Slide ToSlide(this SuggestionItem item) => new()
    {
        Type = SlideType.Scripture,
        Title = item.Title,
        Body = item.Body,
        Footer = item.Footer,
    };

    public static Slide ToSlide(this QueueSlide item) => new()
    {
        Type = item.SlideType,
        Title = item.Title,
        Body = item.Body,
        Footer = item.Footer,
    };
}
