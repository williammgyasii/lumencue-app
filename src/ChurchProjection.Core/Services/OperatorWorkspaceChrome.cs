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
    public const bool AiListeningClipsToRail = true;
    public const bool TranscriptScrollsInsideBox = true;
    public const bool TranscriptStickToLatest = true;
    public const bool TranscriptFillsRemainingRail = true;
    public const bool ShowLastHeardInExpandedPanel = false;
    public const double TranscriptBoxHeight = 88;
    public const double TranscriptBoxMinHeight = 64;
    public const double AiListeningMinBodyHeight = 240;
    public const bool SuggestionsTabShowsStartWhenOff = false;
    public const bool ShowParaphrasesSubtab = false;
    public const bool NowSingingCardsHugContent = true;
    public const bool NowSingingUsesWrapPanel = true;
    public const bool ScriptureCardsHugContent = true;
    public const bool ScriptureUsesWrapPanel = true;
    public const bool FindScriptureCardsHugContent = true;
    public const bool FindScriptureUsesWrapPanel = true;
    public const int ScriptureGridColumns = 3;
    public const double ScriptureListPaddingX = 16;
    public const double ScriptureCardMarginX = 12;
    public const double ScriptureCardMinWidth = 120;
    public const double ScriptureRowSlack = 8;
    public const bool NowSingingToolbarInTabStrip = true;
    public const bool NowSingingShowsInnerTitleBar = false;
    public const bool NowSingingTitleIsBubble = true;
    public const bool NowSingingToolbarOnSongsTab = false;
    public const string NowSingingTitleBubbleBackground = "#3B1D3A";
    public const string NowSingingTitleBubbleForeground = "#F9A8D4";
    public const string NowSingingTitleBubbleBorder = "#F472B6";
    public const double NowSingingTitleBubbleMaxWidth = 220;
    public const string AiListeningBackground = "#12202E";
    public const string AiListeningBorder = "#2B5A7A";
    public const string AiListeningHeaderBackground = "#163044";
    public const string AiListeningAccent = "#38BDF8";

    public static double ScriptureCardWidth(double paneWidth)
    {
        if (paneWidth <= 0) return ScriptureCardMinWidth;
        var inner = paneWidth - ScriptureListPaddingX - ScriptureRowSlack;
        var cell = Math.Floor(inner / ScriptureGridColumns);
        var width = cell - ScriptureCardMarginX;
        return width < ScriptureCardMinWidth ? ScriptureCardMinWidth : width;
    }

    public static double ProgramPreviewMaxHeight(double railHeight)
    {
        if (railHeight <= 0) return 0;
        var cap = railHeight - AiListeningMinBodyHeight;
        return cap < 80 ? 80 : cap;
    }

    public static IReadOnlyList<WorkspaceNavItem> NavItems { get; } =
    [
        new("Bible", "BookOpenPageVariant", true),
        new("Songs", "MusicNote", true),
        new("Media", "PlayBoxOutline", true),
        new("Notes", "NoteTextOutline", true),
        new("Themes", "PaletteOutline", false),
    ];
}
