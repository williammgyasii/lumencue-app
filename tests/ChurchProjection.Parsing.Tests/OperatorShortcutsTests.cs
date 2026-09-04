using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class OperatorShortcutsTests
{
    [Fact]
    public void Digit_two_switches_to_songs()
    {
        Assert.Equal(OperatorShortcutAction.Songs, OperatorShortcuts.Resolve("2"));
    }

    [Fact]
    public void Digit_one_is_bible_not_send_live()
    {
        Assert.Equal(OperatorShortcutAction.Bible, OperatorShortcuts.Resolve("1"));
        Assert.NotEqual(OperatorShortcutAction.SendLive, OperatorShortcuts.Resolve("1"));
    }

    [Fact]
    public void Numpad_three_is_media()
    {
        Assert.Equal(OperatorShortcutAction.Media, OperatorShortcuts.Resolve("NumPad3"));
    }

    [Fact]
    public void O_toggles_output()
    {
        Assert.Equal(OperatorShortcutAction.ToggleOutput, OperatorShortcuts.Resolve("O"));
    }

    [Fact]
    public void Comma_opens_settings()
    {
        Assert.Equal(OperatorShortcutAction.OpenSettings, OperatorShortcuts.Resolve(","));
    }

    [Fact]
    public void Question_mark_toggles_the_cheatsheet()
    {
        Assert.Equal(OperatorShortcutAction.ToggleCheatsheet, OperatorShortcuts.Resolve("?", shift: true));
        Assert.Equal(OperatorShortcutAction.ToggleCheatsheet, OperatorShortcuts.Resolve("?"));
    }

    [Fact]
    public void Text_input_never_runs_the_map()
    {
        Assert.Equal(OperatorShortcutAction.None, OperatorShortcuts.Resolve("1", isTextInput: true));
        Assert.Equal(OperatorShortcutAction.None, OperatorShortcuts.Resolve("O", isTextInput: true));
        Assert.Equal(OperatorShortcutAction.None, OperatorShortcuts.Resolve("?", isTextInput: true));
    }

    [Fact]
    public void Shifted_digit_does_not_change_mode()
    {
        Assert.Equal(OperatorShortcutAction.None, OperatorShortcuts.Resolve("1", shift: true));
    }

    [Fact]
    public void Space_sends_live()
    {
        Assert.Equal(OperatorShortcutAction.SendLive, OperatorShortcuts.Resolve("Space"));
    }

    [Fact]
    public void Escape_dismisses_cheatsheet_instead_of_blanking()
    {
        Assert.Equal(
            OperatorShortcutAction.DismissCheatsheet,
            OperatorShortcuts.Resolve("Escape", cheatsheetVisible: true));
        Assert.Equal(OperatorShortcutAction.Blank, OperatorShortcuts.Resolve("Escape"));
    }
}
