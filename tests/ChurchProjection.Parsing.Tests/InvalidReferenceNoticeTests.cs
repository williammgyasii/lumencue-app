using ChurchProjection.Core.Parsing;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class InvalidReferenceNoticeTests
{
    // A concrete reference (book + chapter) that the lookup found nothing for is a genuine
    // "doesn't exist" — the operator typed e.g. "Genesis 99" (Genesis has only 50 chapters).
    [Fact]
    public void Flags_a_parseable_reference_with_no_results()
    {
        var message = InvalidReferenceNotice.For("Genesis 99", hadResults: false);

        Assert.NotNull(message);
        Assert.Contains("Genesis", message);
    }

    // A specific verse beyond a real chapter ("John 3:999") is still a reference that doesn't exist.
    [Fact]
    public void Flags_a_parseable_verse_with_no_results()
    {
        var message = InvalidReferenceNotice.For("John 3:999", hadResults: false);

        Assert.NotNull(message);
        Assert.Contains("John", message);
    }

    // Plain keyword/topical searches that simply had no hits must NOT be reported as invalid
    // references — "love" is a search, not a missing verse.
    [Fact]
    public void Ignores_non_reference_queries_even_with_no_results()
    {
        Assert.Null(InvalidReferenceNotice.For("love", hadResults: false));
        Assert.Null(InvalidReferenceNotice.For("", hadResults: false));
        Assert.Null(InvalidReferenceNotice.For("   ", hadResults: false));
    }

    // If the lookup actually returned verses, the reference exists — never flag it.
    [Fact]
    public void Ignores_references_that_returned_results()
    {
        Assert.Null(InvalidReferenceNotice.For("John 3:16", hadResults: true));
        Assert.Null(InvalidReferenceNotice.For("Genesis 1", hadResults: true));
    }
}
