using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class VerseAdvanceTests
{
    [Fact]
    public void Step_MovesToTheNextVerse()
    {
        Assert.Equal(3, VerseAdvance.StepIndex(currentIndex: 2, count: 5, direction: +1));
    }

    [Fact]
    public void Step_DoesNotWrapPastTheLastVerse()
    {
        Assert.Equal(4, VerseAdvance.StepIndex(currentIndex: 4, count: 5, direction: +1));
    }

    [Fact]
    public void Step_DoesNotWrapBeforeTheFirstVerse()
    {
        Assert.Equal(0, VerseAdvance.StepIndex(currentIndex: 0, count: 5, direction: -1));
    }

    [Fact]
    public void Step_MovesToThePreviousVerse()
    {
        Assert.Equal(1, VerseAdvance.StepIndex(currentIndex: 2, count: 5, direction: -1));
    }

    [Fact]
    public void Step_ReturnsUnknown_WhenTheLiveVerseIsNotInTheList()
    {
        Assert.Equal(-1, VerseAdvance.StepIndex(currentIndex: -1, count: 5, direction: +1));
    }
}
