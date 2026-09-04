using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class PlaybackClockTests
{
    [Fact]
    public void Formats_elapsed_and_duration_from_milliseconds()
    {
        var clock = PlaybackClock.From(timeMs: 65_000, lengthMs: 185_000, position: 0);

        Assert.Equal("1:05", clock.Elapsed);
        Assert.Equal("3:05", clock.Duration);
    }

    [Fact]
    public void Derives_elapsed_from_position_when_time_ms_is_missing()
    {
        var clock = PlaybackClock.From(timeMs: 0, lengthMs: 120_000, position: 0.25);

        Assert.Equal("0:30", clock.Elapsed);
        Assert.Equal("2:00", clock.Duration);
    }

    [Fact]
    public void Shows_hours_for_long_clips()
    {
        var clock = PlaybackClock.From(timeMs: 3_661_000, lengthMs: 3_661_000, position: 1);

        Assert.Equal("1:01:01", clock.Elapsed);
    }

    [Fact]
    public void Formats_an_eleven_minute_clip()
    {
        var clock = PlaybackClock.From(timeMs: 55_000, lengthMs: 660_000, position: 55.0 / 660.0);

        Assert.Equal("0:55", clock.Elapsed);
        Assert.Equal("11:00", clock.Duration);
    }

    [Fact]
    public void Duration_never_stays_shorter_than_elapsed()
    {
        var clock = PlaybackClock.From(timeMs: 180_000, lengthMs: 55_000, position: 1);

        Assert.Equal("3:00", clock.Elapsed);
        Assert.Equal("3:00", clock.Duration);
    }

    [Fact]
    public void Infers_full_length_when_container_duration_matches_elapsed_mid_clip()
    {
        // 55s elapsed at ~1/12 of the scrubber → real length is 11:00, not 0:55.
        var clock = PlaybackClock.From(timeMs: 55_000, lengthMs: 55_000, position: 55.0 / 660.0);

        Assert.Equal("0:55", clock.Elapsed);
        Assert.Equal("11:00", clock.Duration);
    }
}

public class ScrubSeekPolicyTests
{
    [Fact]
    public void Does_not_seek_when_updating_from_the_player()
    {
        Assert.False(ScrubSeekPolicy.ShouldSeek(incoming: 0.4, lastPolled: 0.39, updatingFromPlayer: true));
    }

    [Fact]
    public void Does_not_seek_for_tiny_slider_writeback()
    {
        Assert.False(ScrubSeekPolicy.ShouldSeek(incoming: 0.401, lastPolled: 0.40, updatingFromPlayer: false));
    }

    [Fact]
    public void Seeks_when_the_operator_scrubs()
    {
        Assert.True(ScrubSeekPolicy.ShouldSeek(incoming: 0.70, lastPolled: 0.20, updatingFromPlayer: false));
    }
}

public class ProgramEmptyHintTests
{
    [Fact]
    public void Hides_when_a_slide_is_live()
    {
        Assert.False(ProgramEmptyHint.IsVisible(slideLive: true, mediaLive: false));
    }

    [Fact]
    public void Hides_when_media_is_live()
    {
        Assert.False(ProgramEmptyHint.IsVisible(slideLive: false, mediaLive: true));
    }

    [Fact]
    public void Shows_mode_specific_detail_when_blank()
    {
        Assert.True(ProgramEmptyHint.IsVisible(slideLive: false, mediaLive: false));
        Assert.Equal("Click a clip to send it live", ProgramEmptyHint.Detail(ProgramEmptyHint.Workspace.Media));
        Assert.Equal("Double-click a verse to go live", ProgramEmptyHint.Detail(ProgramEmptyHint.Workspace.Bible));
    }
}
