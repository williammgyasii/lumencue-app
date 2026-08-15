using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Slides;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class NoteSlidePlannerTests
{
    private const string PastorChrisSample =
        """
        📖 God is faithful, by whom ye were called unto the fellowship of his Son Jesus Christ our Lord (1 Corinthians 1:9).

        ✍️ Pastor Chris says
        I recall the story of a certain minister who placed two chairs on his platform in church.

        It sounded like a nice thing to do but that's not how God wants us to think about fellowship with the Spirit.

        🙏 PRAYER
        Dear Father, I thank you for the indwelling presence of the Holy Spirit in me. Amen.
        """;

    [Fact]
    public void OneParagraphPerSlide_splits_on_blank_lines()
    {
        var slides = NoteSlidePlanner.PlanBodies("Point one\n\nPoint two\n\nPoint three", NoteSplitMode.OneParagraphPerSlide);

        Assert.Equal(["Point one", "Point two", "Point three"], slides);
    }

    [Fact]
    public void PastorChris_sample_yields_one_slide_per_paragraph()
    {
        var slides = NoteSlidePlanner.PlanBodies(PastorChrisSample, NoteSplitMode.OneParagraphPerSlide);

        Assert.Equal(5, slides.Count);
        Assert.StartsWith("📖 God is faithful", slides[0]);
        Assert.StartsWith("✍", slides[1]);
        Assert.StartsWith("I recall the story", slides[2]);
        Assert.StartsWith("It sounded like", slides[3]);
        Assert.StartsWith("🙏 PRAYER", slides[4]);
    }

    [Fact]
    public void BySection_groups_teaching_paragraphs_under_the_section_header()
    {
        var slides = NoteSlidePlanner.PlanBodies(PastorChrisSample, NoteSplitMode.BySection);

        Assert.Equal(5, slides.Count);
        Assert.StartsWith("📖", slides[0]);
        Assert.StartsWith("✍", slides[1]);
        Assert.StartsWith("I recall", slides[2]);
        Assert.StartsWith("It sounded", slides[3]);
        Assert.StartsWith("🙏", slides[4]);
    }

    [Fact]
    public void LinesPerSlide_packs_non_blank_lines_and_wins_over_paragraph_mode()
    {
        var body = "Line one\n\nLine two\n\nLine three\n\nLine four";

        var slides = NoteSlidePlanner.PlanBodies(body, NoteSplitMode.OneParagraphPerSlide, linesPerSlide: 2);

        Assert.Equal(2, slides.Count);
        Assert.Equal("Line one\nLine two", slides[0]);
        Assert.Equal("Line three\nLine four", slides[1]);
    }

    [Fact]
    public void LinesPerSlide_zero_keeps_paragraph_split()
    {
        var slides = NoteSlidePlanner.PlanBodies("Point one\n\nPoint two", NoteSplitMode.OneParagraphPerSlide, 0);

        Assert.Equal(["Point one", "Point two"], slides);
    }

    [Fact]
    public void SplitNoteSections_keeps_header_with_following_content()
    {
        var sections = SlideContentSplitter.SplitNoteSections(PastorChrisSample);

        Assert.Equal(3, sections.Count);
        Assert.StartsWith("📖", sections[0]);
        Assert.StartsWith("✍", sections[1]);
        Assert.Contains("I recall the story", sections[1]);
        Assert.StartsWith("🙏", sections[2]);
    }
}
