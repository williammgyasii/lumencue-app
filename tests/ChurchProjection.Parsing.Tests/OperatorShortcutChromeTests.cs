using System.Linq;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class OperatorShortcutChromeTests
{
    [Fact]
    public void Cheatsheet_lists_the_live_console_map()
    {
        var actions = OperatorShortcutChrome.Rows.Select(r => r.ActionLabel).ToArray();
        Assert.Contains(actions, a => a.Contains("Bible", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, a => a.Contains("Songs", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, a => a.Contains("Media", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, a => a.Contains("Notes", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, a => a.Contains("Output", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, a => a.Contains("Settings", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, a => a.Contains("live", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, a => a.Contains("Blank", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actions, a => a.Contains("Page", System.StringComparison.OrdinalIgnoreCase));
    }
}
