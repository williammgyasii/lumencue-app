namespace ChurchProjection.Core.Services;

/// <summary>
/// Whether a Bible-translation picker change should re-project scripture that is already live.
/// Default on matches today's operator behavior.
/// </summary>
public static class TranslationLiveUpdate
{
    public const string SettingsKey = "update_live_on_translation_change";
    public const bool DefaultEnabled = true;

    public static bool ShouldRefreshLive(bool enabled, bool scriptureIsLive)
        => enabled && scriptureIsLive;
}
