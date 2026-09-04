using System.Collections.Generic;

namespace ChurchProjection.Core.Services;

public readonly record struct WorkspaceNavItem(string Label, string IconKind, bool IsWorkspaceMode);

/// <summary>
/// Operator workspace chrome: Material top-nav, full-box active tab,
/// Bookmarks and Compare as sibling left views, tinted AI Listening under Program.
/// </summary>
public static class OperatorWorkspaceChrome
{
    public const bool HighlightEntireActiveTab = true;
    public const double ActiveOpacity = 1;
    public const double InactiveOpacity = 0.45;
    public const bool CompareUnderBookmarks = false;
    public const bool CompareIsSeparateView = true;
    public const bool CompareClipsToPane = true;
    public const double CompareCardInset = 8;
    public const bool AiListeningUnderProgram = true;
    public const bool ShowBottomAiBar = false;
    public const string AiListeningBackground = "#12202E";
    public const string AiListeningBorder = "#2B5A7A";
    public const string AiListeningHeaderBackground = "#163044";
    public const string AiListeningAccent = "#38BDF8";

    public static IReadOnlyList<WorkspaceNavItem> NavItems { get; } =
    [
        new("Bible", "BookOpenPageVariant", true),
        new("Songs", "MusicNote", true),
        new("Media", "PlayBoxOutline", true),
        new("Notes", "NoteTextOutline", true),
        new("Themes", "PaletteOutline", false),
    ];
}
