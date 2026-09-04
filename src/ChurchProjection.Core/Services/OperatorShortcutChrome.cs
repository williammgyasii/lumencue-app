using System.Collections.Generic;

namespace ChurchProjection.Core.Services;

public readonly record struct ShortcutRow(string KeyLabel, string ActionLabel);

/// <summary>
/// Cheatsheet copy for the operator shortcut overlay. Session UI only.
/// </summary>
public static class OperatorShortcutChrome
{
    public static IReadOnlyList<ShortcutRow> Rows { get; } =
    [
        new("1", "Bible"),
        new("2", "Songs"),
        new("3", "Media"),
        new("4", "Notes"),
        new("O", "Output on/off"),
        new(",", "Settings"),
        new("Space / Enter", "Send live"),
        new("Esc", "Blank"),
        new("← / →", "Page live"),
        new("?", "Show this map"),
    ];
}
