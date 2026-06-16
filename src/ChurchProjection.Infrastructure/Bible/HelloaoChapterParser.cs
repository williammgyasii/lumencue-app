using System.Text.Json;
using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Infrastructure.Bible;

/// <summary>
/// Parses the "content" array of a bible.helloao.org chapter payload into verse passages.
/// Shared by the on-demand client and the bulk cache downloader so the JSON shape is
/// only interpreted in one place.
/// </summary>
internal static class HelloaoChapterParser
{
    /// <summary>
    /// Extracts verses from a chapter <c>content</c> array, optionally filtered to
    /// [<paramref name="verseStart"/>, <paramref name="verseEnd"/>]. When the range is null,
    /// every verse in the chapter is returned.
    /// </summary>
    public static List<ScripturePassage> ParseVerses(
        JsonElement contentArr,
        string translation,
        string book,
        int chapter,
        int? verseStart = null,
        int? verseEnd = null)
    {
        var passages = new List<ScripturePassage>();

        foreach (var item in contentArr.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "verse") continue;
            if (!item.TryGetProperty("number", out var numEl)) continue;

            var verseNum = numEl.ValueKind == JsonValueKind.Number
                ? numEl.GetInt32()
                : int.TryParse(numEl.GetString(), out var parsed) ? parsed : 0;
            if (verseNum == 0) continue;

            if (verseStart.HasValue && (verseNum < verseStart.Value || verseNum > (verseEnd ?? verseStart.Value)))
                continue;

            if (!item.TryGetProperty("content", out var contentEl)) continue;

            var parts = new List<string>();
            foreach (var part in contentEl.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                    parts.Add(part.GetString() ?? "");
                else if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var textProp))
                    parts.Add(textProp.GetString() ?? "");
            }

            var text = string.Join(" ", parts).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            passages.Add(new ScripturePassage
            {
                Translation = translation,
                Book = book,
                Chapter = chapter,
                VerseStart = verseNum,
                VerseEnd = null,
                Text = text,
            });
        }

        return passages;
    }
}
