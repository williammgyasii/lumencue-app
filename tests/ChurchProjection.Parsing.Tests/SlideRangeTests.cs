using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SlideRangeTests
{
    [Fact]
    public void Inclusive_OrdersEitherClickDirection()
    {
        Assert.Equal((2, 5), SlideRange.Inclusive(5, 2));
        Assert.Equal((2, 5), SlideRange.Inclusive(2, 5));
        Assert.Equal((3, 3), SlideRange.Inclusive(3, 3));
    }

    [Fact]
    public void Apply_MarksOnlyTheInclusiveSpan()
    {
        var items = Verses(6);

        SlideRange.Apply(items, fromIndex: 4, toIndex: 1);

        Assert.False(items[0].IsRangeSelected);
        Assert.True(items[1].IsRangeSelected);
        Assert.True(items[2].IsRangeSelected);
        Assert.True(items[3].IsRangeSelected);
        Assert.True(items[4].IsRangeSelected);
        Assert.False(items[5].IsRangeSelected);
    }

    [Fact]
    public void Apply_ClearsPreviousMarksOutsideTheNewSpan()
    {
        var items = Verses(4);
        items[0].IsRangeSelected = true;
        items[3].IsRangeSelected = true;

        SlideRange.Apply(items, fromIndex: 1, toIndex: 2);

        Assert.False(items[0].IsRangeSelected);
        Assert.True(items[1].IsRangeSelected);
        Assert.True(items[2].IsRangeSelected);
        Assert.False(items[3].IsRangeSelected);
    }

    private static ContentItem[] Verses(int count)
    {
        var items = new ContentItem[count];
        for (var i = 0; i < count; i++)
            items[i] = new ContentItem { Title = $"v{i + 1}" };
        return items;
    }
}
