using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class LiveClickPolicyTests
{
    [Fact]
    public void SingleClick_PreviewsOnly_WhenTheLiveSettingIsOff()
    {
        Assert.False(LiveClickPolicy.GoesLive(isDoubleClick: false, singleClickGoesLive: false));
    }

    [Fact]
    public void DoubleClick_AlwaysSendsLive()
    {
        Assert.True(LiveClickPolicy.GoesLive(isDoubleClick: true, singleClickGoesLive: false));
        Assert.True(LiveClickPolicy.GoesLive(isDoubleClick: true, singleClickGoesLive: true));
    }

    [Fact]
    public void SingleClick_SendsLive_WhenTheLiveSettingIsOn()
    {
        Assert.True(LiveClickPolicy.GoesLive(isDoubleClick: false, singleClickGoesLive: true));
    }
}
