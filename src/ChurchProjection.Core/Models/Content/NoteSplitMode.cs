namespace ChurchProjection.Core.Models.Content;

/// <summary>How a note body is broken into projected slides.</summary>
public enum NoteSplitMode
{
    /// <summary>Pack paragraphs onto slides when they fit the theme (legacy behaviour).</summary>
    AutoFit = 0,

    /// <summary>Each blank-line-separated paragraph becomes its own slide.</summary>
    OneParagraphPerSlide = 1,

    /// <summary>Split on section headers (📖 / ✍️ / 🙏 / ALL CAPS), then one slide per paragraph within each section.</summary>
    BySection = 2,
}
