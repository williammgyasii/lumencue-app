namespace ChurchProjection.Core.Models.Slides;

public enum SlideType
{
    Blank,
    Scripture,
    Lyric,
    Announcement,
    Media,
    Countdown,
    Clock,
    Note
}

public class Slide
{
    public SlideType Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Footer { get; init; } = string.Empty;

    /// <summary>Absolute UTC target the projector counts down to (Countdown slides only).</summary>
    public DateTime? CountdownTargetUtc { get; init; }

    /// <summary>Time format string used by Clock slides (e.g. "h:mm tt").</summary>
    public string? ClockFormat { get; init; }

    public static Slide Blank() => new() { Type = SlideType.Blank };

    /// <summary>A self-ticking countdown that reads its remaining time from <paramref name="targetUtc"/>.</summary>
    public static Slide Countdown(DateTime targetUtc, string heading, string doneMessage) => new()
    {
        Type = SlideType.Countdown,
        Title = heading,
        Footer = doneMessage,
        CountdownTargetUtc = targetUtc
    };

    /// <summary>A self-ticking wall clock.</summary>
    public static Slide Clock(string heading, string format = "h:mm tt") => new()
    {
        Type = SlideType.Clock,
        Title = heading,
        ClockFormat = format
    };

    public static Slide FromText(string title, string body) => new()
    {
        Type = SlideType.Announcement,
        Title = title,
        Body = body
    };

    /// <summary>A free-text note (e.g. prayer points). Styled by the Note theme on the projector.</summary>
    public static Slide FromNote(string title, string body) => new()
    {
        Type = SlideType.Note,
        Title = title,
        Body = body
    };
}
