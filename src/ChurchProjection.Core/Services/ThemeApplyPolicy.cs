using ChurchProjection.Core.Models.Theme;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Verse-to-verse go-live must not rebuild the theme (decode images, recreate shapes)
/// when the look has not changed. That rebuild is what makes image and motion
/// backgrounds feel laggy on double-click.
/// </summary>
public static class ThemeApplyPolicy
{
    public static bool NeedsFullApply(Theme? applied, Theme incoming, bool leavingBlank)
        => leavingBlank || !ReferenceEquals(applied, incoming);
}
