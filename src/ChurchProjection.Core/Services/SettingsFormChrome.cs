using System.Collections.Generic;

namespace ChurchProjection.Core.Services;

public readonly record struct SettingsNavItem(string Label, string IconKind);

/// <summary>
/// Fixed Settings dialog. Form width is the content pane, never larger —
/// window minus sidebar minus horizontal padding.
/// </summary>
public static class SettingsFormChrome
{
    public const bool CanResize = false;
    public const double WindowWidth = 780;
    public const double WindowHeight = 620;
    public const double FooterHeight = 64;
    public const double SidebarWidth = 200;
    public const double ContentPaddingX = 40;
    public const double ComboColumnWidth = 200;
    public const double ToggleColumnWidth = 40;
    public const double ThemeColumnWidth = 160;
    public const bool ScreensUseSingleRow = true;

    public static double ContentWidth => WindowWidth - SidebarWidth - ContentPaddingX;
    public static double ContentPaneHeight => WindowHeight - FooterHeight;

    public static IReadOnlyList<SettingsNavItem> NavItems { get; } =
    [
        new("Display", "Monitor"),
        new("Behavior", "Tune"),
        new("Screens", "ProjectorScreen"),
        new("ProPresenter", "Cast"),
        new("About", "InformationOutline"),
    ];
}
