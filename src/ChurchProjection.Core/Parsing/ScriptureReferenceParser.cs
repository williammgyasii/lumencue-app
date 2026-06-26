using System.Text.RegularExpressions;
using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Parsing;

public static partial class ScriptureReferenceParser
{
    private static readonly Dictionary<string, string> BookAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gen"] = "Genesis", ["ge"] = "Genesis", ["gn"] = "Genesis", ["genesis"] = "Genesis",
        ["ex"] = "Exodus", ["exod"] = "Exodus", ["exodus"] = "Exodus",
        ["lev"] = "Leviticus", ["le"] = "Leviticus",
        ["num"] = "Numbers", ["nu"] = "Numbers", ["nm"] = "Numbers",
        ["deut"] = "Deuteronomy", ["de"] = "Deuteronomy", ["dt"] = "Deuteronomy",
        ["josh"] = "Joshua", ["jos"] = "Joshua",
        ["judg"] = "Judges", ["jdg"] = "Judges",
        ["ruth"] = "Ruth", ["ru"] = "Ruth",
        ["1sam"] = "1 Samuel", ["1sa"] = "1 Samuel",
        ["2sam"] = "2 Samuel", ["2sa"] = "2 Samuel",
        ["1ki"] = "1 Kings", ["1kgs"] = "1 Kings",
        ["2ki"] = "2 Kings", ["2kgs"] = "2 Kings",
        ["1chr"] = "1 Chronicles", ["1ch"] = "1 Chronicles",
        ["2chr"] = "2 Chronicles", ["2ch"] = "2 Chronicles",
        ["ezr"] = "Ezra",
        ["neh"] = "Nehemiah", ["ne"] = "Nehemiah",
        ["est"] = "Esther",
        ["job"] = "Job", ["jb"] = "Job",
        ["ps"] = "Psalms", ["psa"] = "Psalms", ["psalm"] = "Psalms", ["psalms"] = "Psalms",
        ["prov"] = "Proverbs", ["pr"] = "Proverbs", ["prv"] = "Proverbs",
        ["eccl"] = "Ecclesiastes", ["ec"] = "Ecclesiastes",
        ["song"] = "Song of Solomon", ["sos"] = "Song of Solomon", ["sg"] = "Song of Solomon",
        ["isa"] = "Isaiah", ["is"] = "Isaiah",
        ["jer"] = "Jeremiah", ["je"] = "Jeremiah",
        ["lam"] = "Lamentations", ["la"] = "Lamentations",
        ["ezek"] = "Ezekiel", ["eze"] = "Ezekiel",
        ["dan"] = "Daniel", ["da"] = "Daniel", ["dn"] = "Daniel",
        ["hos"] = "Hosea", ["ho"] = "Hosea",
        ["joel"] = "Joel", ["jl"] = "Joel",
        ["amos"] = "Amos", ["am"] = "Amos",
        ["obad"] = "Obadiah", ["ob"] = "Obadiah",
        ["jonah"] = "Jonah", ["jon"] = "Jonah",
        ["mic"] = "Micah", ["mi"] = "Micah",
        ["nah"] = "Nahum", ["na"] = "Nahum",
        ["hab"] = "Habakkuk",
        ["zeph"] = "Zephaniah", ["zep"] = "Zephaniah",
        ["hag"] = "Haggai",
        ["zech"] = "Zechariah", ["zec"] = "Zechariah",
        ["mal"] = "Malachi",
        ["mat"] = "Matthew", ["matt"] = "Matthew", ["mt"] = "Matthew",
        ["mark"] = "Mark", ["mk"] = "Mark", ["mr"] = "Mark",
        ["luke"] = "Luke", ["lk"] = "Luke", ["lu"] = "Luke",
        ["john"] = "John", ["jn"] = "John", ["joh"] = "John",
        ["acts"] = "Acts", ["ac"] = "Acts",
        ["rom"] = "Romans", ["ro"] = "Romans",
        ["1cor"] = "1 Corinthians", ["1co"] = "1 Corinthians",
        ["2cor"] = "2 Corinthians", ["2co"] = "2 Corinthians",
        ["gal"] = "Galatians", ["ga"] = "Galatians",
        ["eph"] = "Ephesians",
        ["phil"] = "Philippians", ["php"] = "Philippians", ["philippians"] = "Philippians",
        ["col"] = "Colossians", ["colossians"] = "Colossians",
        ["1thess"] = "1 Thessalonians", ["1th"] = "1 Thessalonians",
        ["2thess"] = "2 Thessalonians", ["2th"] = "2 Thessalonians",
        ["1tim"] = "1 Timothy", ["1ti"] = "1 Timothy",
        ["2tim"] = "2 Timothy", ["2ti"] = "2 Timothy",
        ["titus"] = "Titus", ["tit"] = "Titus",
        ["phlm"] = "Philemon", ["phm"] = "Philemon",
        ["heb"] = "Hebrews", ["hebrews"] = "Hebrews",
        ["jas"] = "James", ["jm"] = "James", ["james"] = "James",
        ["1pet"] = "1 Peter", ["1pe"] = "1 Peter", ["1pt"] = "1 Peter",
        ["2pet"] = "2 Peter", ["2pe"] = "2 Peter", ["2pt"] = "2 Peter",
        ["1jn"] = "1 John", ["1jo"] = "1 John",
        ["2jn"] = "2 John", ["2jo"] = "2 John",
        ["3jn"] = "3 John", ["3jo"] = "3 John",
        ["jude"] = "Jude", ["jud"] = "Jude",
        ["rev"] = "Revelation", ["re"] = "Revelation", ["revelation"] = "Revelation",
    };

    private static readonly HashSet<string> FullBookNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy",
        "Joshua", "Judges", "Ruth", "1 Samuel", "2 Samuel",
        "1 Kings", "2 Kings", "1 Chronicles", "2 Chronicles",
        "Ezra", "Nehemiah", "Esther", "Job", "Psalms", "Proverbs",
        "Ecclesiastes", "Song of Solomon", "Isaiah", "Jeremiah",
        "Lamentations", "Ezekiel", "Daniel", "Hosea", "Joel", "Amos",
        "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk", "Zephaniah",
        "Haggai", "Zechariah", "Malachi",
        "Matthew", "Mark", "Luke", "John", "Acts", "Romans",
        "1 Corinthians", "2 Corinthians", "Galatians", "Ephesians",
        "Philippians", "Colossians", "1 Thessalonians", "2 Thessalonians",
        "1 Timothy", "2 Timothy", "Titus", "Philemon", "Hebrews",
        "James", "1 Peter", "2 Peter", "1 John", "2 John", "3 John",
        "Jude", "Revelation"
    };

    // "John 3:16" or "John 3:16-18"
    [GeneratedRegex(
        @"^(?<book>(?:[123]\s*)?[A-Za-z]+(?:\s+of\s+[A-Za-z]+)?)\s+(?<chapter>\d{1,3})\s*:\s*(?<vstart>\d{1,3})(?:\s*[-–—]\s*(?<vend>\d{1,3}))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VersePattern();

    // Colon-free shorthand: "mat 1 1" → Matthew 1:1, "mat 1 1-5" / "mat 1 1 5" → Matthew 1:1-5.
    // Lets operators type a reference quickly without reaching for the colon mid-service.
    [GeneratedRegex(
        @"^(?<book>(?:[123]\s*)?[A-Za-z]+(?:\s+of\s+[A-Za-z]+)?)\s+(?<chapter>\d{1,3})\s+(?<vstart>\d{1,3})(?:\s*[-–—\s]\s*(?<vend>\d{1,3}))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SpaceVersePattern();

    // "John 3" — whole chapter
    [GeneratedRegex(
        @"^(?<book>(?:[123]\s*)?[A-Za-z]+(?:\s+of\s+[A-Za-z]+)?)\s+(?<chapter>\d{1,3})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ChapterPattern();

    // For scanning text that may contain multiple refs inline
    [GeneratedRegex(
        @"(?<book>(?:[123]\s*)?[A-Za-z]+(?:\s+of\s+[A-Za-z]+)?)\s+(?<chapter>\d{1,3})\s*:\s*(?<vstart>\d{1,3})(?:\s*[-–—]\s*(?<vend>\d{1,3}))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InlineReferencePattern();

    public static ScriptureReference? TryParse(string input) => TryParse(input, allowFuzzyBook: false);

    /// <param name="allowFuzzyBook">
    /// When true, an unrecognised book token is matched to the nearest canonical book within a small
    /// edit distance ("matew" → Matthew). Enabled only for the TYPED search box; the live spoken/AI
    /// path leaves it false so free speech is never auto-corrected into a false reference.
    /// </param>
    public static ScriptureReference? TryParse(string input, bool allowFuzzyBook)
    {
        var trimmed = input.Trim();
        Func<string, string?> resolveBook = allowFuzzyBook ? NormalizeBookFuzzy : NormalizeBook;

        // Try verse-level first: "John 3:16"
        var match = VersePattern().Match(trimmed);
        if (match.Success)
        {
            var book = resolveBook(match.Groups["book"].Value.Trim());
            if (book is null) return null;

            return new ScriptureReference(
                book,
                int.Parse(match.Groups["chapter"].Value),
                int.Parse(match.Groups["vstart"].Value),
                match.Groups["vend"].Success ? int.Parse(match.Groups["vend"].Value) : null);
        }

        // Try colon-free shorthand: "mat 1 1" → Matthew 1:1
        var spaceMatch = SpaceVersePattern().Match(trimmed);
        if (spaceMatch.Success)
        {
            var book = resolveBook(spaceMatch.Groups["book"].Value.Trim());
            if (book is not null)
            {
                return new ScriptureReference(
                    book,
                    int.Parse(spaceMatch.Groups["chapter"].Value),
                    int.Parse(spaceMatch.Groups["vstart"].Value),
                    spaceMatch.Groups["vend"].Success ? int.Parse(spaceMatch.Groups["vend"].Value) : null);
            }
        }

        // Try chapter-level: "John 3" → whole chapter (verse 1 to 200 as sentinel)
        var chapterMatch = ChapterPattern().Match(trimmed);
        if (chapterMatch.Success)
        {
            var book = resolveBook(chapterMatch.Groups["book"].Value.Trim());
            if (book is null) return null;

            return new ScriptureReference(
                book,
                int.Parse(chapterMatch.Groups["chapter"].Value),
                VerseStart: 1,
                VerseEnd: 200);
        }

        return null;
    }

    /// <summary>
    /// True when the input reads as a reference the operator is still typing — a book (or fuzzy book)
    /// token followed only by whitespace/digits, but not yet a complete, parseable reference. The
    /// typed search uses this to skip the semantic phrase fallback (which otherwise surfaces unrelated
    /// "central" verses like John 1:1 for a half-typed fragment). Reference intent requires a trailing
    /// space or a number after the book, so a bare word ("love", "job") stays an ordinary word search.
    /// </summary>
    public static bool LooksLikePartialReference(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        // A complete reference is not "partial" — let the normal lookup handle it.
        if (TryParse(input, allowFuzzyBook: true) is not null) return false;

        var hasTrailingSpace = char.IsWhiteSpace(input[^1]);
        var tokens = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        var bookTokens = ResolveLeadingBook(tokens, out var book);
        if (book is null) return false;

        // Everything after the book must be digits (chapter/verse being typed).
        for (var i = bookTokens; i < tokens.Length; i++)
            if (!tokens[i].All(char.IsDigit))
                return false;

        var hasNumberPart = tokens.Length > bookTokens;
        return hasNumberPart || hasTrailingSpace;
    }

    private static int ResolveLeadingBook(string[] tokens, out string? book)
    {
        book = null;
        if (tokens.Length == 0) return 0;

        // Numbered books span two tokens ("1 john"); only combine when the 2nd token isn't itself a
        // number, so we never swallow a chapter into the book ("mark 3").
        if (tokens.Length >= 2 && !tokens[1].All(char.IsDigit))
        {
            var combined = NormalizeBookFuzzy(tokens[0] + " " + tokens[1]);
            if (combined is not null)
            {
                book = combined;
                return 2;
            }
        }

        var single = NormalizeBookFuzzy(tokens[0]);
        if (single is not null)
        {
            book = single;
            return 1;
        }

        return 0;
    }

    /// <summary>Resolves a book token allowing small typos, falling back to <see cref="NormalizeBook"/>
    /// for exact/alias/prefix matches first. Returns the nearest canonical book within a length-scaled
    /// edit distance, but only when that nearest book is unambiguous (no ties).</summary>
    public static string? NormalizeBookFuzzy(string raw)
    {
        var exact = NormalizeBook(raw);
        if (exact is not null) return exact;

        var key = raw.Replace(".", "").Replace(" ", "").Trim().ToLowerInvariant();
        if (key.Length < 3) return null; // too short to disambiguate safely

        var threshold = key.Length <= 4 ? 1 : 2;

        string? best = null;
        var bestDistance = int.MaxValue;
        var tied = false;

        foreach (var canonical in FullBookNames)
        {
            var candidate = canonical.Replace(" ", "").ToLowerInvariant();
            var distance = Levenshtein(key, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = canonical;
                tied = false;
            }
            else if (distance == bestDistance)
            {
                tied = true;
            }
        }

        return best is not null && bestDistance <= threshold && !tied ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }

    public static List<ScriptureReference> ExtractAll(string text)
    {
        var results = new List<ScriptureReference>();
        foreach (Match match in InlineReferencePattern().Matches(text))
        {
            var rawBook = match.Groups["book"].Value.Trim();
            var book = NormalizeBook(rawBook);
            if (book is null) continue;

            var chapter = int.Parse(match.Groups["chapter"].Value);
            var verseStart = int.Parse(match.Groups["vstart"].Value);
            int? verseEnd = match.Groups["vend"].Success
                ? int.Parse(match.Groups["vend"].Value)
                : null;

            results.Add(new ScriptureReference(book, chapter, verseStart, verseEnd));
        }
        return results;
    }

    public static List<ScriptureReference> ExtractFromSpoken(string spokenText)
    {
        var normalized = NormalizeSpokenText(spokenText);
        var results = ExtractAll(normalized);
        if (results.Count > 0) return results;

        var parsed = TryParse(normalized);
        if (parsed is not null)
        {
            results.Add(parsed);
            return results;
        }

        return TryExtractSpokenReferences(normalized);
    }

    private static string NormalizeSpokenText(string text)
    {
        var lower = text.ToLowerInvariant();

        lower = Regex.Replace(lower, @"\b(first|1st)\b", "1");
        lower = Regex.Replace(lower, @"\b(second|2nd)\b", "2");
        lower = Regex.Replace(lower, @"\b(third|3rd)\b", "3");

        lower = Regex.Replace(lower, @"\bchapter\b", " ");
        lower = Regex.Replace(lower, @"\bverses?\b", ":");
        lower = Regex.Replace(lower, @"\b(let us |let's |turn to |go to |look at |open |read )", " ");
        lower = Regex.Replace(lower, @"\b(says?|said|tells? us|we read)\b", " ");
        lower = Regex.Replace(lower, @"\b(the book of|book of)\b", " ");

        lower = ReplaceSpokenNumbers(lower);
        lower = CoalesceSpelledDigits(lower);

        lower = Regex.Replace(lower, @"\s+", " ").Trim();
        return lower;
    }

    /// <summary>
    /// Joins a run of consecutive single spoken digits into one number when the speaker is clearly
    /// spelling a number out digit-by-digit — e.g. "1 0 9" (spoken "one zero nine") → "109",
    /// "1 1 9" → "119". Speech engines often emit these as separate single-digit tokens, which the
    /// positional parser would otherwise read as chapter+verse and miss the real reference.
    ///
    /// Deliberately conservative: a run is merged only when it contains a 0 (never a valid standalone
    /// chapter or verse) or is three-or-more digits long. A two-digit run with no zero ("3 6") is left
    /// untouched so a genuine "chapter three verse six" is not mangled into 36.
    /// </summary>
    public static string CoalesceSpelledDigits(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        var run = new List<string>();

        void FlushRun()
        {
            if (run.Count == 0) return;
            var hasZero = run.Contains("0");
            if (run.Count >= 3 || (run.Count >= 2 && hasZero))
                result.Add(string.Concat(run));
            else
                result.AddRange(run);
            run.Clear();
        }

        foreach (var token in tokens)
        {
            if (token.Length == 1 && char.IsDigit(token[0]))
            {
                run.Add(token);
            }
            else
            {
                FlushRun();
                result.Add(token);
            }
        }
        FlushRun();

        return string.Join(" ", result);
    }

    /// <summary>Converts spoken cardinal numbers ("twenty three") into digits ("23"), leaving other
    /// words untouched. Exposed so the progressive spoken-reference builder can reuse it.</summary>
    public static string ReplaceSpokenNumbers(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var result = new List<string>();

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i].TrimEnd(',', '.', ';');
            if (WordToNumber.TryGetValue(w, out int val))
            {
                int compound = val;
                while (i + 1 < words.Count)
                {
                    var next = words[i + 1].TrimEnd(',', '.', ';');
                    if (WordToNumber.TryGetValue(next, out int nextVal))
                    {
                        if (val >= 100 && nextVal < 100)
                            compound = val + nextVal;
                        else if (val >= 20 && nextVal < 10)
                            compound = val + nextVal;
                        else
                            break;
                        val = nextVal;
                        i++;
                    }
                    else break;
                }
                result.Add(compound.ToString());
            }
            else
            {
                result.Add(words[i]);
            }
        }
        return string.Join(" ", result);
    }

    private static List<ScriptureReference> TryExtractSpokenReferences(string text)
    {
        var results = new List<ScriptureReference>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < words.Length; i++)
        {
            string? bookName = null;
            int bookEndIdx = i;

            var candidate = words[i].TrimEnd(',', '.', ';');
            if (candidate.Length >= 1 && char.IsDigit(candidate[0]) && i + 1 < words.Length)
            {
                var twoWord = candidate + " " + words[i + 1].TrimEnd(',', '.', ';');
                bookName = NormalizeBook(twoWord);
                if (bookName is not null) bookEndIdx = i + 1;
            }

            if (bookName is null)
            {
                bookName = NormalizeBook(candidate);
                if (bookName is not null) bookEndIdx = i;
            }

            if (bookName is null) continue;

            int chapter = -1, verseStart = -1;
            int? verseEnd = null;
            int nextIdx = bookEndIdx + 1;

            if (nextIdx < words.Length && int.TryParse(words[nextIdx].TrimEnd(',', '.', ';', ':'), out int num1))
            {
                var raw = words[nextIdx].TrimEnd(',', '.', ';');
                if (raw.Contains(':'))
                {
                    var parts = raw.Split(':');
                    if (int.TryParse(parts[0], out int ch) && parts.Length > 1 && int.TryParse(parts[1].TrimEnd('-'), out int vs)
                        && IsPlausibleChapter(ch) && IsPlausibleVerse(vs))
                    {
                        chapter = ch;
                        verseStart = vs;
                    }
                }
                else
                {
                    // A stray large number ("Isaiah 8206" from mis-formatted spoken numerals) is not a
                    // real chapter — skip it rather than firing a doomed whole-chapter fetch.
                    if (!IsPlausibleChapter(num1)) continue;

                    chapter = num1;
                    nextIdx++;

                    if (nextIdx < words.Length && words[nextIdx].TrimEnd(',', '.', ';') == ":")
                        nextIdx++;

                    if (nextIdx < words.Length && int.TryParse(words[nextIdx].TrimEnd(',', '.', ';', '-'), out int num2)
                        && IsPlausibleVerse(num2))
                    {
                        verseStart = num2;
                        nextIdx++;

                        if (nextIdx < words.Length)
                        {
                            var dashOrNum = words[nextIdx].TrimEnd(',', '.', ';');
                            if (dashOrNum.StartsWith('-') || dashOrNum.StartsWith("through"))
                            {
                                nextIdx++;
                                if (nextIdx < words.Length && int.TryParse(words[nextIdx].TrimEnd(',', '.', ';'), out int num3))
                                    verseEnd = num3;
                            }
                            else if (int.TryParse(dashOrNum, out int maybeEnd) && maybeEnd > verseStart && maybeEnd < verseStart + 30)
                            {
                                verseEnd = maybeEnd;
                            }
                        }
                    }
                }
            }

            if (chapter > 0)
            {
                results.Add(new ScriptureReference(
                    bookName, chapter,
                    verseStart > 0 ? verseStart : 1,
                    verseStart > 0 ? verseEnd : 200));
                i = nextIdx - 1;
            }
        }

        return results;
    }

    // Upper bounds match the Bible's extremes (150 chapters in Psalms, 176 verses in Psalm 119), so
    // any larger number from mis-formatted spoken numerals is rejected rather than parsed as a ref.
    private static bool IsPlausibleChapter(int n) => n is >= 1 and <= 150;
    private static bool IsPlausibleVerse(int n) => n is >= 1 and <= 176;

    private static readonly Dictionary<string, int> WordToNumber = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4,
        ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70,
        ["eighty"] = 80, ["ninety"] = 90, ["hundred"] = 100,
    };

    // A few safe spoken variants for the strict matcher. Deliberately excludes the short/common-word
    // aliases (e.g. "is"→Isaiah, "am"→Amos, "mic"→Micah) that would fire constantly on free speech.
    private static readonly Dictionary<string, string> SafeSpokenBookAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["psalm"] = "Psalms",
        ["song"] = "Song of Solomon",
        ["songs"] = "Song of Solomon",
        ["song of songs"] = "Song of Solomon",
        ["revelations"] = "Revelation",
    };

    /// <summary>
    /// Strict book resolver for matching free-flowing speech: only exact canonical book names and a
    /// curated set of safe spoken variants match. Unlike <see cref="NormalizeBook"/> it does NO
    /// prefix/abbreviation guessing, so ordinary words ("number", "mark it", "is") are not mistaken
    /// for books. Returns null when the token is not a confident book name.
    /// </summary>
    public static string? NormalizeBookStrict(string raw)
    {
        var cleaned = raw.Replace(".", "").Trim();
        if (cleaned.Length == 0) return null;

        if (FullBookNames.Contains(cleaned))
            return FullBookNames.First(b => b.Equals(cleaned, StringComparison.OrdinalIgnoreCase));

        return SafeSpokenBookAliases.GetValueOrDefault(cleaned);
    }

    /// <summary>Resolves a raw book token (alias, abbreviation or full name) to its canonical book
    /// name, or null when it is not a recognisable book. Exposed for the spoken-reference builder.</summary>
    public static string? NormalizeBook(string raw)
    {
        var cleaned = raw.Replace(".", "").Trim();

        if (FullBookNames.Contains(cleaned))
            return FullBookNames.First(b => b.Equals(cleaned, StringComparison.OrdinalIgnoreCase));

        var key = cleaned.Replace(" ", "").ToLowerInvariant();
        if (BookAliases.TryGetValue(key, out var full))
            return full;

        if (key.Length >= 3)
        {
            var match = FullBookNames
                .Where(b => b.StartsWith(cleaned, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (match.Count == 1)
                return match[0];
        }

        return null;
    }
}
