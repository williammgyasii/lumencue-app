using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SongSlideInsertTests
{
    [Fact]
    public void After_NullAnchor_AppendsAVerseAndResequences()
    {
        var sections = new List<SongSection>
        {
            new() { SectionType = "verse", SectionOrder = 1, Text = "v1" },
            new() { SectionType = "chorus", SectionOrder = 1, Text = "c" },
        };

        var created = SongSlideInsert.After(sections, after: null, "verse", "new lines");

        Assert.Equal(3, sections.Count);
        Assert.Same(created, sections[^1]);
        Assert.Equal("verse", created.SectionType);
        Assert.Equal("new lines", created.Text);
        Assert.Equal([1, 2, 3], sections.Select(s => s.SectionOrder));
    }

    [Fact]
    public void After_SelectedSection_InsertsImmediatelyAfterIt()
    {
        var verse1 = new SongSection { SectionType = "verse", SectionOrder = 1, Text = "v1" };
        var chorus = new SongSection { SectionType = "chorus", SectionOrder = 1, Text = "c" };
        var sections = new List<SongSection> { verse1, chorus };

        var created = SongSlideInsert.After(sections, verse1, "verse", "bridge line");

        Assert.Equal([verse1, created, chorus], sections);
        Assert.Equal([1, 2, 3], sections.Select(s => s.SectionOrder));
    }

    [Fact]
    public void After_BlankType_DefaultsToVerse()
    {
        var sections = new List<SongSection>();

        var created = SongSlideInsert.After(sections, after: null, "  ", "");

        Assert.Equal("verse", created.SectionType);
        Assert.Equal("", created.Text);
        Assert.Equal(1, created.SectionOrder);
    }
}
