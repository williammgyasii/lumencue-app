using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class BiblePrefetchIndexTests
{
    [Fact]
    public void Follow_on_picker_code_is_not_indexed()
    {
        Assert.False(BiblePrefetchIndex.ShouldIndex("NKJV", "NIV"));
    }

    [Fact]
    public void Selected_translation_is_indexed()
    {
        Assert.True(BiblePrefetchIndex.ShouldIndex("NIV", "NIV"));
    }
}
