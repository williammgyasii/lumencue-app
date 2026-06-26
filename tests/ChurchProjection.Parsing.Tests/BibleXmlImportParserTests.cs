using ChurchProjection.Infrastructure.Bible;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// Parses the numbered-book XML format used by the bundled translation files (e.g. the Passion
/// Translation). Books carry only a 1-based <c>number</c> in canonical order, some verses are
/// intentionally empty (the translation merges them into a neighbour), and the verse text must be
/// preserved as-is apart from trimming.
/// </summary>
public class BibleXmlImportParserTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bible translation="English Passion Translation Bible 2020">
            <testament name="Old">
                <book number="1">
                    <chapter number="1">
                        <verse number="1">When God created the heavens and the earth,</verse>
                        <verse number="2">the earth was formless and empty.</verse>
                        <verse number="3"></verse>
                    </chapter>
                    <chapter number="2">
                        <verse number="1">  And so the creation was completed.  </verse>
                    </chapter>
                </book>
            </testament>
            <testament name="New">
                <book number="40">
                    <chapter number="1">
                        <verse number="1">The book of the genealogy of Jesus the Anointed One.</verse>
                    </chapter>
                </book>
            </testament>
        </bible>
        """;

    [Fact]
    public void Reads_the_source_translation_name_from_the_root()
    {
        var result = BibleXmlImportParser.Parse(SampleXml);

        Assert.Equal("English Passion Translation Bible 2020", result.SourceName);
    }

    [Fact]
    public void Maps_numbered_books_to_canonical_names()
    {
        var result = BibleXmlImportParser.Parse(SampleXml);

        // Book 1 = Genesis, book 40 = Matthew (canonical order).
        Assert.Contains(result.Verses, v => v.Book == "Genesis" && v.Chapter == 1 && v.Verse == 1);
        Assert.Contains(result.Verses, v => v.Book == "Matthew" && v.Chapter == 1 && v.Verse == 1);
    }

    [Fact]
    public void Skips_intentionally_empty_verses()
    {
        var result = BibleXmlImportParser.Parse(SampleXml);

        // Genesis 1:3 is empty in the source and must not be imported.
        Assert.DoesNotContain(result.Verses, v => v.Book == "Genesis" && v.Chapter == 1 && v.Verse == 3);
    }

    [Fact]
    public void Trims_surrounding_whitespace_but_preserves_inner_text()
    {
        var result = BibleXmlImportParser.Parse(SampleXml);

        var genesis2 = Assert.Single(result.Verses, v => v.Book == "Genesis" && v.Chapter == 2 && v.Verse == 1);
        Assert.Equal("And so the creation was completed.", genesis2.Text);
    }

    [Fact]
    public void Counts_only_non_empty_verses()
    {
        var result = BibleXmlImportParser.Parse(SampleXml);

        // Gen 1:1, Gen 1:2, Gen 2:1, Matt 1:1 — the empty Gen 1:3 is dropped.
        Assert.Equal(4, result.Verses.Count);
    }

    [Fact]
    public void Builds_a_hosted_file_with_code_name_and_verses()
    {
        var file = BibleXmlImportParser.ToCustomBibleFile(SampleXml, code: "TPT", name: "The Passion Translation");

        Assert.Equal("TPT", file.Code);
        Assert.Equal("The Passion Translation", file.Name);
        Assert.Equal(4, file.Verses.Count);

        // Round-trips through the shared JSON shape the app will read.
        var roundTripped = CustomBibleFile.FromJson(file.ToJson());
        Assert.Equal(file.Verses.Count, roundTripped.Verses.Count);
        Assert.Contains(roundTripped.Verses, v => v.Book == "Matthew" && v.Chapter == 1 && v.Verse == 1);
    }

    [Fact]
    public void Rejects_book_numbers_outside_the_canon()
    {
        var badXml = """
            <bible translation="X">
                <testament name="New"><book number="67"><chapter number="1">
                    <verse number="1">beyond Revelation</verse>
                </chapter></book></testament>
            </bible>
            """;

        Assert.Throws<InvalidOperationException>(() => BibleXmlImportParser.Parse(badXml));
    }
}
