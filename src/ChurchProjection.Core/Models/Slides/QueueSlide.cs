namespace ChurchProjection.Core.Models.Slides;

/// <summary>An entry in the service queue, ready to be projected.</summary>
public class QueueSlide
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Footer { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Icon { get; set; } = "";
    public SlideType SlideType { get; set; }

    /// <summary>Per-song lyric lines-per-slide override (0 = theme auto-fit).</summary>
    public int LinesPerSlide { get; set; }
}
