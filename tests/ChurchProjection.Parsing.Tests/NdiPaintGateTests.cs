using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// Booth stand-in: 15 ticks/sec with no Windows + OBS. A lyric sitting still for 10s
/// is 150 ticks. After warmup we must almost never paint.
/// </summary>
public class NdiPaintGateTests
{
    [Fact]
    public void First_ticks_warm_up_then_static_lyrics_skip()
    {
        var gate = new NdiPaintGate();

        for (var i = 0; i < 150; i++)
        {
            if (gate.ShouldPaint(skipUnchanged: true, motionLive: false))
                gate.MarkPainted();
        }

        Assert.Equal(NdiPaintGate.WarmupPaints, gate.PaintCount);
        Assert.Equal(150 - NdiPaintGate.WarmupPaints, gate.SkipCount);
        Assert.True(gate.SkipCount > gate.PaintCount * 10);
    }

    [Fact]
    public void Slide_change_paints_again()
    {
        var gate = new NdiPaintGate();
        if (gate.ShouldPaint(true, false)) gate.MarkPainted();
        gate.NoteChanged();

        Assert.True(gate.ShouldPaint(skipUnchanged: true, motionLive: false));
    }

    [Fact]
    public void Motion_keeps_painting_after_warmup()
    {
        var gate = new NdiPaintGate();
        for (var i = 0; i < NdiPaintGate.WarmupPaints; i++)
        {
            if (gate.ShouldPaint(true, motionLive: true))
                gate.MarkPainted();
        }

        Assert.True(gate.ShouldPaint(skipUnchanged: true, motionLive: true));
    }
}
