namespace ChurchProjection.Core.Services;

/// <summary>
/// Same-computer OBS + NDI cannot afford a 1080p Skia paint on every timer tick.
/// Skip must run before Render — hashing pixels after paint still freezes the booth.
/// </summary>
public static class NdiFramePaintPolicy
{
    public static bool MustPaint(bool skipUnchanged, bool contentChanged, bool motionLive)
    {
        if (!skipUnchanged) return true;
        return contentChanged || motionLive;
    }
}
