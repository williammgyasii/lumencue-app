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

    /// <summary>The 66 canonical books in order (Genesis = index 0 … Revelation = index 65). Used to
    /// resolve the 1-based book numbers in bundled translation XML files.</summary>
    public static IReadOnlyList<string> InCanonicalOrder { get; } = NameToId.Keys.ToArray();

    /// <summary>Resolves a 1-based canonical book number (1 = Genesis … 66 = Revelation) to its name,
    /// or null when the number is outside the 66-book canon.</summary>
    public static string? NameForNumber(int number) =>
        number >= 1 && number <= InCanonicalOrder.Count ? InCanonicalOrder[number - 1] : null;

    public static bool TryGetId(string bookName, out string id) => NameToId.TryGetValue(bookName, out id!);

    public static string GetName(string bookId, string? fallback = null) =>
        IdToName.TryGetValue(bookId, out var name) ? name : fallback ?? bookId;

    /// <summary>Number of chapters in a canonical book (0 when unknown).</summary>
    public static int ChapterCountFor(string bookName) =>
        ChapterCounts.TryGetValue(bookName, out var count) ? count : 0;

    /// <summary>Standard chapter counts for the 66-book Protestant canon.</summary>
    public static IReadOnlyDictionary<string, int> ChapterCounts { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Genesis"] = 50, ["Exodus"] = 40, ["Leviticus"] = 27, ["Numbers"] = 36, ["Deuteronomy"] = 34,
        ["Joshua"] = 24, ["Judges"] = 21, ["Ruth"] = 4, ["1 Samuel"] = 31, ["2 Samuel"] = 24,
        ["1 Kings"] = 22, ["2 Kings"] = 25, ["1 Chronicles"] = 29, ["2 Chronicles"] = 36, ["Ezra"] = 10,
        ["Nehemiah"] = 13, ["Esther"] = 10, ["Job"] = 42, ["Psalms"] = 150, ["Proverbs"] = 31,
        ["Ecclesiastes"] = 12, ["Song of Solomon"] = 8, ["Isaiah"] = 66, ["Jeremiah"] = 52, ["Lamentations"] = 5,
        ["Ezekiel"] = 48, ["Daniel"] = 12, ["Hosea"] = 14, ["Joel"] = 3, ["Amos"] = 9,
        ["Obadiah"] = 1, ["Jonah"] = 4, ["Micah"] = 7, ["Nahum"] = 3, ["Habakkuk"] = 3,
        ["Zephaniah"] = 3, ["Haggai"] = 2, ["Zechariah"] = 14, ["Malachi"] = 4,
        ["Matthew"] = 28, ["Mark"] = 16, ["Luke"] = 24, ["John"] = 21, ["Acts"] = 28,
        ["Romans"] = 16, ["1 Corinthians"] = 16, ["2 Corinthians"] = 13, ["Galatians"] = 6, ["Ephesians"] = 6,
        ["Philippians"] = 4, ["Colossians"] = 4, ["1 Thessalonians"] = 5, ["2 Thessalonians"] = 3,
        ["1 Timothy"] = 6, ["2 Timothy"] = 4, ["Titus"] = 3, ["Philemon"] = 1, ["Hebrews"] = 13,
        ["James"] = 5, ["1 Peter"] = 5, ["2 Peter"] = 3, ["1 John"] = 5, ["2 John"] = 1,
        ["3 John"] = 1, ["Jude"] = 1, ["Revelation"] = 22,
    };
}
