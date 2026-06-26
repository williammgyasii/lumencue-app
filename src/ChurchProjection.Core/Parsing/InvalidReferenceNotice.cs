using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Parsing;

/// <summary>
/// Decides whether an operator-typed query that returned no scripture results should be reported as
/// a reference that "doesn't exist". Only fires when the query clearly parses as a concrete
/// reference (book + chapter); plain keyword searches with no hits must never be flagged as invalid.
/// </summary>
public static class InvalidReferenceNotice
{
    /// <summary>
    /// Returns a user-facing message when <paramref name="query"/> parses as a real scripture
    /// reference but produced no results; otherwise null.
    /// </summary>
    public static string? For(string query, bool hadResults)
    {
        // Results came back → the reference exists, nothing to report.
        if (hadResults) return null;
        if (string.IsNullOrWhiteSpace(query)) return null;

        // Only a query that parses as a concrete reference (book + chapter) is a candidate. A plain
        // keyword search like "love" returns null here, so it is never mislabeled as a missing verse.
        var reference = ScriptureReferenceParser.TryParse(query);
        if (reference is null) return null;

        // Render chapter-only references as "Book Chapter" rather than the internal whole-chapter
        // sentinel span ("Genesis 99:1-200").
        var label = reference.VerseEnd is >= ScriptureReference.WholeChapterSentinel
            ? $"{reference.Book} {reference.Chapter}"
            : reference.ToShortString();

        return $"{label} isn't in the Bible";
    }
}
