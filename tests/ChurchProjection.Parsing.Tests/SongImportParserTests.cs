using ChurchProjection.Infrastructure.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace ChurchProjection.Parsing.Tests;

public class SongImportParserTests
{
    private readonly ITestOutputHelper _output;

    public SongImportParserTests(ITestOutputHelper output) => _output = output;

    // The exact kind of paste a worship operator drops in: every lyric line on its own line,
    // but NO blank lines separating stanzas. Previously this produced 22 identical 4-line chunks.
    private const string NumberOneRaw = """
        First things first
        You are not another option
        Take Your place
        You are my Treasure and my Priority
        You will always be my Number One, I will never place before You another
        I remove every idol, break them before You
        You are my King, You're my only One
        I remove every idol, break them before You
        You are my King, You're my only One
        You will always be my Number One
        I will never place before You another
        You will always be
        You will always be my Number One
        Right now anything else must go
        Everything else that sits on the throne of my heart
        Only You belong there, Only You belong there
        You will always be my Number One
        I will never place before You another
        You will always be
        You will always be my Number One
        You are the only One
        Who died for me
        Who set me free
        Gave me victory
        Every idol must go
        Every idol must go
        You will always be the only One for me
        You will always be the only One for me
        You will always be the only One for me
        You will always be my Number One
        I will never place before You another
        You will always be
        You will always be my Number One
        Take Your place on the throne of my heart
        Take Your place on the throne of my heart
        Take Your place on the throne of my heart
        Jesus take Your place on the throne of my heart
        I have made You too small in my eyes
        I have made You too small in my eyes
        You will always be my Number One
        I will never place before You another
        You will always be
        You will always be my Number One
        I surrender all to You
        Everything I have I lay down
        You can have it all I am yours
        I belong to You Lord
        You will always be my Number One
        I will never place before You another
        You will always be
        You will always be my Number One
        """;

    [Fact]
    public void RealPaste_NoBlankLines_DetectsRepeatingChorus()
    {
        var sections = SongImportParser.ParseSections(NumberOneRaw);

        _output.WriteLine($"Produced {sections.Count} sections:");
        foreach (var s in sections)
            _output.WriteLine($"  [{s.Label}] {s.Text.Replace("\n", " / ")}");

        // The old behavior produced ~13 fixed 4-line chunks with no chorus recognized.
        var choruses = sections.Where(s => s.SectionType == "chorus").ToList();

        // The 4-line chorus repeats many times — it must be recognized as a chorus, not chopped up.
        Assert.True(choruses.Count >= 3, $"Expected the repeating chorus to be detected 3+ times, got {choruses.Count}.");

        // Every detected chorus is the same block of lyrics...
        var distinctChorusText = choruses.Select(c => c.Text).Distinct().ToList();
        Assert.Single(distinctChorusText);

        // ...and it is the real chorus.
        Assert.Contains("Number One", distinctChorusText[0]);
        Assert.Contains("I will never place before You another", distinctChorusText[0]);

        // There must be verses too — it didn't just label everything chorus.
        Assert.Contains(sections, s => s.SectionType == "verse");
    }

    [Fact]
    public void CleanCase_VerseChorusVerseChorus_SegmentsExactly()
    {
        const string raw = """
            Amazing grace how sweet the sound
            That saved a wretch like me
            Praise the Lord with all my heart
            Praise His holy name forever
            Sing of mercy ever flowing
            How precious is His love
            Praise the Lord with all my heart
            Praise His holy name forever
            """;

        var sections = SongImportParser.ParseSections(raw);

        // Expect: verse, chorus, verse, chorus.
        Assert.Equal(4, sections.Count);
        Assert.Equal(new[] { "verse", "chorus", "verse", "chorus" }, sections.Select(s => s.SectionType).ToArray());

        var choruses = sections.Where(s => s.SectionType == "chorus").Select(s => s.Text).ToList();
        Assert.Equal(2, choruses.Count);
        Assert.Single(choruses.Distinct());
        Assert.Contains("Praise the Lord", choruses[0]);
    }

    [Fact]
    public void LabeledLyrics_StillHonorExplicitHeaders()
    {
        const string raw = """
            Verse 1
            Line one of verse
            Line two of verse

            Chorus
            This is the chorus line
            And another chorus line
            """;

        var sections = SongImportParser.ParseSections(raw);

        Assert.Equal(2, sections.Count);
        Assert.Equal("verse", sections[0].SectionType);
        Assert.Equal("chorus", sections[1].SectionType);
    }

    [Fact]
    public void NoRepetition_FallsBackToChunking_WithoutCrashing()
    {
        const string raw = """
            One unique opening thought
            Another different idea here
            Something else entirely new
            A fourth distinct sentence
            Yet another unrelated line
            And one final unique phrase
            """;

        var sections = SongImportParser.ParseSections(raw);

        // No repeats → no chorus, but it must still produce reasonable verse blocks (not one giant blob, not crash).
        Assert.NotEmpty(sections);
        Assert.All(sections, s => Assert.Equal("verse", s.SectionType));
    }
}
