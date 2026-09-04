namespace ChurchProjection.Core.Services;

/// <summary>
/// Avalonia leaves the owner control in pointer-over while a modal is up.
/// The trigger tooltip then flickers against the dialog — hide it for the duration.
/// </summary>
public static class OwnerDialogTooltips
{
    public const string SettingsTip = "Settings";
    public const string ThemesTip = "Design themes — one look for scripture, songs, notes, and announcements";

    public static object? TipWhileOpen(bool dialogOpen, object? saved)
        => dialogOpen ? null : saved;
}
