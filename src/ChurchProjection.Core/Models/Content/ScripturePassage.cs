namespace ChurchProjection.Core.Models.Content;

public class ScripturePassage
{
    public long Id { get; set; }
    public string Translation { get; set; } = "KJV";
    public string Book { get; set; } = string.Empty;
    public int Chapter { get; set; }
    public int VerseStart { get; set; }
    public int? VerseEnd { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ApiBibleId { get; set; }
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsWholeChapter => VerseStart == 1 && VerseEnd is >= 176;

    public string Reference => IsWholeChapter
        ? $"{Book} {Chapter}"
        : VerseEnd.HasValue && VerseEnd.Value != VerseStart
            ? $"{Book} {Chapter}:{VerseStart}-{VerseEnd}"
            : $"{Book} {Chapter}:{VerseStart}";

    public string DisplayText => $"{Reference}\n\n{Text}";
}
