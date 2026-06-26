using System.Xml.Linq;
using ChurchProjection.Core.Bible;

namespace ChurchProjection.Infrastructure.Bible;

/// <summary>A single verse parsed from a bundled translation XML file.</summary>
public record ImportedVerse(string Book, int Chapter, int Verse, string Text);

/// <summary>The result of parsing a bundled translation file: its self-declared source name and the
/// flat list of non-empty verses, with books resolved to canonical names.</summary>
public record BibleImport(string SourceName, IReadOnlyList<ImportedVerse> Verses);

/// <summary>
/// Parses the numbered-book Bible XML format (<c>bible &gt; testament &gt; book[number] &gt;
/// chapter[number] &gt; verse[number]</c>) used by the bundled translation files such as the Passion
/// Translation. Books carry only a 1-based canonical number, so they are mapped to names via
/// <see cref="BibleBooks.InCanonicalOrder"/>. Intentionally-empty verses (the translation merges some
/// verses into a neighbour) are skipped; verse text is trimmed but otherwise preserved as authored.
/// </summary>
public static class BibleXmlImportParser
{
    public static BibleImport ParseFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses the XML and projects it into the hosted <see cref="CustomBibleFile"/> shape the
    /// desktop app downloads and caches.</summary>
    public static CustomBibleFile ToCustomBibleFile(string xml, string code, string name)
    {
        var import = Parse(xml);
        var verses = import.Verses
            .Select(v => new CustomBibleVerse(v.Book, v.Chapter, v.Verse, v.Text))
            .ToList();
        return new CustomBibleFile(code, name, verses);
    }

    public static BibleImport Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root
            ?? throw new InvalidOperationException("The translation XML has no root element.");

        var sourceName = (string?)root.Attribute("translation") ?? "Unknown";
        var verses = new List<ImportedVerse>();

        foreach (var bookEl in root.Descendants("book"))
        {
            var number = (int?)bookEl.Attribute("number")
                ?? throw new InvalidOperationException("A <book> element is missing its number attribute.");

            var bookName = BibleBooks.NameForNumber(number)
                ?? throw new InvalidOperationException(
                    $"Book number {number} is outside the 66-book canon; the file may not be a standard Protestant Bible.");

            foreach (var chapterEl in bookEl.Elements("chapter"))
            {
                var chapter = (int?)chapterEl.Attribute("number")
                    ?? throw new InvalidOperationException($"A <chapter> in {bookName} is missing its number attribute.");

                foreach (var verseEl in chapterEl.Elements("verse"))
                {
                    var verseNumber = (int?)verseEl.Attribute("number")
                        ?? throw new InvalidOperationException($"A <verse> in {bookName} {chapter} is missing its number attribute.");

                    var text = verseEl.Value.Trim();
                    if (text.Length == 0)
                        continue; // intentionally-merged/blank verse

                    verses.Add(new ImportedVerse(bookName, chapter, verseNumber, text));
                }
            }
        }

        return new BibleImport(sourceName, verses);
    }
}
