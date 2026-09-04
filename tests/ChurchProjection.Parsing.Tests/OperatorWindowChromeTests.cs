using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class OperatorWindowChromeTests
{
    [Fact]
    public void Operator_window_is_a_normal_resizable_desktop_window()
    {
        Assert.True(OperatorWindowChrome.CanResize);
        Assert.False(OperatorWindowChrome.ExtendClientArea);
        Assert.False(OperatorWindowChrome.StartsMaximized);
    }
}
