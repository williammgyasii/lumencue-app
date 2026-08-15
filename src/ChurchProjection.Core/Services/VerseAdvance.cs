namespace ChurchProjection.Core.Services;

/// <summary>
/// Steps through a loaded chapter's verse list. Never wraps: going next on the last verse
/// stays there, instead of jumping back to verse 1 (the old queue-rollover bug).
/// </summary>
public static class VerseAdvance
{
    /// <summary>
    /// Returns the next index, the same index when already at that end, or -1 when
    /// <paramref name="currentIndex"/> is not a valid position in the list.
    /// </summary>
    public static int StepIndex(int currentIndex, int count, int direction)
    {
        if (count <= 0 || currentIndex < 0 || currentIndex >= count)
            return -1;

        var next = currentIndex + (direction >= 0 ? 1 : -1);
        if (next < 0 || next >= count)
            return currentIndex;
        return next;
    }
}
