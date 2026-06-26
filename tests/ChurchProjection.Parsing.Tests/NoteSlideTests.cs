using ChurchProjection.Core.Models.Slides;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// Notes (prayer points) project as their own slide type so they can carry a distinct theme, and the
/// body splits on blank lines so each point/paragraph is a unit the deck builder can paginate.
/// </summary>
public class NoteSlideTests
{
    [Fact]
    public void FromNote_produces_a_note_typed_slide()
    {
        var slide = Slide.FromNote("Prayer Points", "For the nation\n\nFor the sick");

        Assert.Equal(SlideType.Note, slide.Type);
        Assert.Equal("Prayer Points", slide.Title);
        Assert.Equal("For the nation\n\nFor the sick", slide.Body);
    }

    [Fact]
    public void Note_body_splits_into_one_block_per_paragraph()
    {
        var blocks = SlideContentSplitter.SplitBlocks(SlideType.Note, "Point one\n\nPoint two\n\nPoint three");

        Assert.Equal(["Point one", "Point two", "Point three"], blocks);
    }

    [Fact]
    public void A_single_block_note_stays_whole()
    {
        var blocks = SlideContentSplitter.SplitBlocks(SlideType.Note, "Just one prayer point");

        Assert.Equal(["Just one prayer point"], blocks);
    }
}
