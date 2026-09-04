using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class TranslationLiveUpdateTests
{
    [Fact]
    public void Refreshes_live_when_enabled_and_scripture_is_on_screen()
    {
        Assert.True(TranslationLiveUpdate.ShouldRefreshLive(enabled: true, scriptureIsLive: true));
    }

    [Fact]
    public void Skips_live_when_the_operator_turned_the_setting_off()
    {
        Assert.False(TranslationLiveUpdate.ShouldRefreshLive(enabled: false, scriptureIsLive: true));
    }

    [Fact]
    public void Skips_live_when_nothing_scriptural_is_live()
    {
        Assert.False(TranslationLiveUpdate.ShouldRefreshLive(enabled: true, scriptureIsLive: false));
    }

    [Fact]
    public void Defaults_to_updating_live()
    {
        Assert.True(TranslationLiveUpdate.DefaultEnabled);
    }
}
