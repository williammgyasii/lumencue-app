using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class NoteSlideEditTests
{
    [Fact]
    public void Replace_swaps_the_page_at_index()
    {
        var next = NoteSlideEdit.Replace(["one", "two", "three"], 1, "TWO");

        Assert.Equal(["one", "TWO", "three"], next);
    }

    [Fact]
    public void InsertAfter_adds_a_page_after_the_anchor()
    {
        var next = NoteSlideEdit.InsertAfter(["one", "two"], 0, "1.5");

        Assert.Equal(["one", "1.5", "two"], next);
    }

    [Fact]
    public void Join_uses_blank_lines_when_not_packing_by_line_count()
    {
        Assert.Equal("one\n\ntwo", NoteSlideEdit.Join(["one", "two"], linesPerSlide: 0));
    }

    [Fact]
    public void Join_uses_single_newlines_when_packing_by_line_count()
    {
        Assert.Equal("one\ntwo", NoteSlideEdit.Join(["one", "two"], linesPerSlide: 2));
    }
}
