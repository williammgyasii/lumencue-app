namespace ChurchProjection.Core.Models.Content;

public record ScriptureReference(
    string Book,
    int Chapter,
    int VerseStart,
    int? VerseEnd = null)
{
    /// <summary>
    /// <see cref="VerseEnd"/> value meaning "through the end of the chapter". Chosen larger
    /// than any real chapter length (Psalm 119 has 176 verses, the most in the Bible).
    /// </summary>
    public const int WholeChapterSentinel = 200;

    public string ToShortString() => VerseEnd.HasValue && VerseEnd.Value != VerseStart
        ? $"{Book} {Chapter}:{VerseStart}-{VerseEnd}"
        : $"{Book} {Chapter}:{VerseStart}";

    public override string ToString() => ToShortString();
}
