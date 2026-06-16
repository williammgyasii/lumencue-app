namespace ChurchProjection.Core.Bible;

/// <summary>
/// Canonical mapping between Bible book names and their 3-letter USFM ids
/// (e.g. "Genesis" &lt;-&gt; "GEN"). Single source of truth shared by all Bible clients.
/// </summary>
public static class BibleBooks
{
    public static IReadOnlyDictionary<string, string> NameToId { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Genesis"] = "GEN", ["Exodus"] = "EXO", ["Leviticus"] = "LEV",
        ["Numbers"] = "NUM", ["Deuteronomy"] = "DEU", ["Joshua"] = "JOS",
        ["Judges"] = "JDG", ["Ruth"] = "RUT", ["1 Samuel"] = "1SA",
        ["2 Samuel"] = "2SA", ["1 Kings"] = "1KI", ["2 Kings"] = "2KI",
        ["1 Chronicles"] = "1CH", ["2 Chronicles"] = "2CH", ["Ezra"] = "EZR",
        ["Nehemiah"] = "NEH", ["Esther"] = "EST", ["Job"] = "JOB",
        ["Psalms"] = "PSA", ["Proverbs"] = "PRO", ["Ecclesiastes"] = "ECC",
        ["Song of Solomon"] = "SNG", ["Isaiah"] = "ISA", ["Jeremiah"] = "JER",
        ["Lamentations"] = "LAM", ["Ezekiel"] = "EZK", ["Daniel"] = "DAN",
        ["Hosea"] = "HOS", ["Joel"] = "JOL", ["Amos"] = "AMO",
        ["Obadiah"] = "OBA", ["Jonah"] = "JON", ["Micah"] = "MIC",
        ["Nahum"] = "NAM", ["Habakkuk"] = "HAB", ["Zephaniah"] = "ZEP",
        ["Haggai"] = "HAG", ["Zechariah"] = "ZEC", ["Malachi"] = "MAL",
        ["Matthew"] = "MAT", ["Mark"] = "MRK", ["Luke"] = "LUK",
        ["John"] = "JHN", ["Acts"] = "ACT", ["Romans"] = "ROM",
        ["1 Corinthians"] = "1CO", ["2 Corinthians"] = "2CO",
        ["Galatians"] = "GAL", ["Ephesians"] = "EPH", ["Philippians"] = "PHP",
        ["Colossians"] = "COL", ["1 Thessalonians"] = "1TH", ["2 Thessalonians"] = "2TH",
        ["1 Timothy"] = "1TI", ["2 Timothy"] = "2TI", ["Titus"] = "TIT",
        ["Philemon"] = "PHM", ["Hebrews"] = "HEB", ["James"] = "JAS",
        ["1 Peter"] = "1PE", ["2 Peter"] = "2PE", ["1 John"] = "1JN",
        ["2 John"] = "2JN", ["3 John"] = "3JN", ["Jude"] = "JUD",
        ["Revelation"] = "REV",
    };

    public static IReadOnlyDictionary<string, string> IdToName { get; } =
        NameToId.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetId(string bookName, out string id) => NameToId.TryGetValue(bookName, out id!);

    public static string GetName(string bookId, string? fallback = null) =>
        IdToName.TryGetValue(bookId, out var name) ? name : fallback ?? bookId;
}
