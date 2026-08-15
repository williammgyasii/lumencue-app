namespace ChurchProjection.Core.Models.Content;

/// <summary>
/// A free-text note the operator can project — e.g. prayer points. A note is just a title and a body;
/// the body may contain several lines/paragraphs, which the deck builder paginates to fit the screen.
/// </summary>
public class Note
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NoteSplitMode SplitMode { get; set; } = NoteSplitMode.OneParagraphPerSlide;
    /// <summary>0 = use <see cref="SplitMode"/> / theme fit. 1–8 packs that many lines per slide.</summary>
    public int LinesPerSlide { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
