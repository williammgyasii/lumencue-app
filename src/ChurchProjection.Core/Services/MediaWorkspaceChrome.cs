namespace ChurchProjection.Core.Services;

/// <summary>
/// Media is a playback workspace. AI listening and live backgrounds stay available, but they
/// start collapsed so the bin and transport get the height.
/// </summary>
public static class MediaWorkspaceChrome
{
    public static bool UtilityBarsExpanded(bool isMediaMode) => !isMediaMode;
}
