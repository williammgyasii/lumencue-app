namespace ChurchProjection.Core.Services;

/// <summary>Formats a live clip's elapsed / duration for the transport readout.</summary>
public readonly record struct PlaybackClock(string Elapsed, string Duration)
{
    public static PlaybackClock From(long timeMs, long lengthMs, double position)
    {
        var elapsedMs = timeMs > 0
            ? timeMs
            : lengthMs > 0 && position > 0
                ? (long)Math.Round(position * lengthMs)
                : 0;

        var durationMs = lengthMs;
        if (elapsedMs > 0 && position is > 0.04 and < 0.96)
        {
            var inferred = (long)Math.Round(elapsedMs / position);
            if (inferred > durationMs)
                durationMs = inferred;
        }

        if (elapsedMs > durationMs)
            durationMs = elapsedMs;

        return new PlaybackClock(Format(elapsedMs), Format(durationMs));
    }

    private static string Format(long ms)
    {
        if (ms <= 0) return "0:00";
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }
}

/// <summary>
/// Decides whether a scrubber Value write should seek. Avalonia sliders write back a near-equal
/// value after we paint the player position; those write-backs must not seek, or the clock stalls.
/// </summary>
public static class ScrubSeekPolicy
{
    public const double DeadZone = 0.015;

    public static bool ShouldSeek(double incoming, double lastPolled, bool updatingFromPlayer)
        => !updatingFromPlayer && Math.Abs(incoming - lastPolled) > DeadZone;
}

/// <summary>When the Program monitor shows the empty-state hint, and what it says.</summary>
public static class ProgramEmptyHint
{
    public enum Workspace { Bible, Songs, Media, Notes }

    public static bool IsVisible(bool slideLive, bool mediaLive) => !slideLive && !mediaLive;

    public static string Detail(Workspace workspace) => workspace switch
    {
        Workspace.Media => "Click a clip to send it live",
        Workspace.Songs => "Double-click a slide to go live",
        Workspace.Notes => "Double-click a note slide to go live",
        _ => "Double-click a verse to go live",
    };
}
