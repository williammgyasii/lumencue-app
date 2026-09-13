using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class NdiFramePaintPolicyTests
{
    [Fact]
    public void Skip_unchanged_is_on()
    {
        Assert.True(NdiCaptureWindowChrome.SkipUnchanged);
    }

    [Fact]
    public void Static_unchanged_tick_must_not_paint()
    {
        Assert.False(NdiFramePaintPolicy.MustPaint(skipUnchanged: true, contentChanged: false, motionLive: false));
    }

    [Fact]
    public void Changed_content_must_paint()
    {
        Assert.True(NdiFramePaintPolicy.MustPaint(skipUnchanged: true, contentChanged: true, motionLive: false));
    }

    [Fact]
    public void Motion_must_paint_even_when_generation_is_unchanged()
    {
        Assert.True(NdiFramePaintPolicy.MustPaint(skipUnchanged: true, contentChanged: false, motionLive: true));
    }
}
