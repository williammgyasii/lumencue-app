namespace ChurchProjection.Core.Services;

public enum OperatorShortcutAction
{
    None,
    Bible,
    Songs,
    Media,
    Notes,
    ToggleOutput,
    OpenSettings,
    ToggleCheatsheet,
    DismissCheatsheet,
    SendLive,
    Blank,
    PageForward,
    PageBack
}

/// <summary>
/// Fixed operator-window keymap. Tokens are layout-neutral ("1", "O", "?", "Space").
/// Avalonia key codes stay in the window.
/// </summary>
public static class OperatorShortcuts
{
    public static OperatorShortcutAction Resolve(
        string token,
        bool shift = false,
        bool ctrl = false,
        bool alt = false,
        bool meta = false,
        bool isTextInput = false,
        bool cheatsheetVisible = false)
    {
        if (isTextInput || ctrl || alt || meta)
            return OperatorShortcutAction.None;

        var key = Normalize(token);

        if (key == "Escape")
            return cheatsheetVisible
                ? OperatorShortcutAction.DismissCheatsheet
                : OperatorShortcutAction.Blank;

        if (key is "Space" or "Enter" or "Return")
            return OperatorShortcutAction.SendLive;

        if (key == "Left")
            return OperatorShortcutAction.PageBack;
        if (key == "Right")
            return OperatorShortcutAction.PageForward;

        if (key == "?")
            return OperatorShortcutAction.ToggleCheatsheet;

        if (shift)
            return OperatorShortcutAction.None;

        return key switch
        {
            "1" => OperatorShortcutAction.Bible,
            "2" => OperatorShortcutAction.Songs,
            "3" => OperatorShortcutAction.Media,
            "4" => OperatorShortcutAction.Notes,
            "O" => OperatorShortcutAction.ToggleOutput,
            "," => OperatorShortcutAction.OpenSettings,
            _ => OperatorShortcutAction.None
        };
    }

    private static string Normalize(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.StartsWith("NumPad", System.StringComparison.OrdinalIgnoreCase)
            && token.Length > "NumPad".Length)
            return token["NumPad".Length..];

        return token.Length == 1 ? token.ToUpperInvariant() : token;
    }
}
