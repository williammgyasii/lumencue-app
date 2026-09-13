namespace ChurchProjection.Core.Services;

/// <summary>
/// Counts paint vs skip for NDI ticks. Warmup paints twice so the first
/// Avalonia frame is not stuck black on OBS, then static lyrics skip.
/// </summary>
public sealed class NdiPaintGate
{
    public const int WarmupPaints = 2;

    private long _generation = 1;
    private long _painted = -1;
    private int _warmupLeft = WarmupPaints;

    public int PaintCount { get; private set; }
    public int SkipCount { get; private set; }

    public void NoteChanged() => _generation++;

    public bool ShouldPaint(bool skipUnchanged, bool motionLive)
    {
        var contentChanged = _painted != _generation;
        if (_warmupLeft > 0 || NdiFramePaintPolicy.MustPaint(skipUnchanged, contentChanged, motionLive))
            return true;

        SkipCount++;
        return false;
    }

    public void MarkPainted()
    {
        _painted = _generation;
        PaintCount++;
        if (_warmupLeft > 0)
            _warmupLeft--;
    }
}
