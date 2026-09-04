using System.Linq;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SettingsFormChromeTests
{
    [Fact]
    public void Form_fits_inside_the_content_pane()
    {
        var pane = SettingsFormChrome.WindowWidth
            - SettingsFormChrome.SidebarWidth
            - SettingsFormChrome.ContentPaddingX;
        Assert.Equal(pane, SettingsFormChrome.ContentWidth);
        Assert.True(SettingsFormChrome.ContentWidth <= pane);
        Assert.False(SettingsFormChrome.CanResize);
        Assert.Equal(780, SettingsFormChrome.WindowWidth);
        Assert.Equal(620, SettingsFormChrome.WindowHeight);
    }

    [Fact]
    public void Content_pane_is_shorter_than_the_window_so_pages_can_scroll()
    {
        Assert.Equal(
            SettingsFormChrome.WindowHeight - SettingsFormChrome.FooterHeight,
            SettingsFormChrome.ContentPaneHeight);
        Assert.True(SettingsFormChrome.ContentPaneHeight < SettingsFormChrome.WindowHeight);
        Assert.True(SettingsFormChrome.ContentPaneHeight > 400);
    }

    [Fact]
    public void Reserved_control_columns_are_narrower_than_the_form()
    {
        Assert.Equal(200, SettingsFormChrome.ComboColumnWidth);
        Assert.Equal(40, SettingsFormChrome.ToggleColumnWidth);
        Assert.True(SettingsFormChrome.ComboColumnWidth < SettingsFormChrome.ContentWidth);
        Assert.True(SettingsFormChrome.ToggleColumnWidth < SettingsFormChrome.ContentWidth);
    }

    [Fact]
    public void Screen_outputs_use_a_single_row()
    {
        Assert.True(SettingsFormChrome.ScreensUseSingleRow);
        Assert.Equal(160, SettingsFormChrome.ThemeColumnWidth);
        Assert.True(
            SettingsFormChrome.ToggleColumnWidth
            + SettingsFormChrome.ThemeColumnWidth
            + 120
            <= SettingsFormChrome.ContentWidth);
    }

    [Fact]
    public void Sidebar_items_have_an_icon_and_a_label()
    {
        Assert.Equal(5, SettingsFormChrome.NavItems.Count);
        Assert.All(SettingsFormChrome.NavItems, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Label));
            Assert.False(string.IsNullOrWhiteSpace(item.IconKind));
        });
        Assert.Equal(new[] { "Display", "Behavior", "Screens", "ProPresenter", "About" },
            SettingsFormChrome.NavItems.Select(i => i.Label).ToArray());
    }
}
