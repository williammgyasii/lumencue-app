namespace ChurchProjection.Core.Models.Theme;

/// <summary>
/// Theme Studio only offers Solid / Image / Placeholder. ATEM key colours stay in the
/// runtime enum for imported themes, but the editor treats them as Solid + that colour.
/// </summary>
public static class ThemeBackgroundStudio
{
    public static readonly ThemeBackgroundKind[] EditorTypes =
    [
        ThemeBackgroundKind.Solid,
        ThemeBackgroundKind.Image,
        ThemeBackgroundKind.Placeholder,
    ];

    public static ThemeBackgroundKind ForEditor(ThemeBackgroundKind kind) => kind switch
    {
        ThemeBackgroundKind.KeyColorGreen or ThemeBackgroundKind.KeyColorBlack => ThemeBackgroundKind.Solid,
        _ => kind,
    };

    public static bool ShowsColorPicker(ThemeBackgroundKind kind)
        => ForEditor(kind) == ThemeBackgroundKind.Solid;

    public static bool ShowsImagePicker(ThemeBackgroundKind kind)
        => ForEditor(kind) == ThemeBackgroundKind.Image;

    public static string EditorColor(Theme theme) => theme.BackgroundKind switch
    {
        ThemeBackgroundKind.KeyColorGreen => Theme.KeyGreen,
        ThemeBackgroundKind.KeyColorBlack => Theme.KeyBlack,
        _ => theme.BackgroundColor,
    };

    /// <summary>
    /// Writes the inspector colour. Key-colour imports become Solid so the operator can
    /// customise them; Image / Placeholder stay put so a hidden colour picker write-back
    /// cannot snap Type back to Solid.
    /// </summary>
    public static void ApplyEditorColor(Theme theme, string hex)
    {
        if (theme.BackgroundKind is ThemeBackgroundKind.KeyColorGreen or ThemeBackgroundKind.KeyColorBlack)
            theme.BackgroundKind = ThemeBackgroundKind.Solid;
        theme.BackgroundColor = hex;
    }
}
