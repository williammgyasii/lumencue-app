using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class BibleCacheQueueTests
{
    [Fact]
    public void Selected_leads_the_queue()
    {
        var queue = BibleCacheQueue.Order("NIV", ["KJV", "NIV"]);

        Assert.Equal("NIV", queue[0]);
    }

    [Fact]
    public void Picker_codes_follow_selected_without_duplicates()
    {
        var queue = BibleCacheQueue.Order("NIV", ["KJV", "NIV", "ESV"]);

        Assert.Equal(["NIV", "KJV", "ESV"], queue);
    }

    [Fact]
    public void Non_picker_English_is_omitted()
    {
        var queue = BibleCacheQueue.Order("KJV", ["KJV", "NIV"]);

        Assert.DoesNotContain("WEB", queue);
        Assert.Equal(["KJV", "NIV"], queue);
    }
}
