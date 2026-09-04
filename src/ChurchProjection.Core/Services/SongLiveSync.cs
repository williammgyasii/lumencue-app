using System;
using System.Collections.Generic;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Whether a saved song edit may rewrite the lyric slide that is already live.
/// Only the same song + the same section/page may refresh; otherwise leave the projector alone.
/// </summary>
public static class SongLiveSync
{
    public readonly record struct SectionKey(string SectionType, int SectionOrder, int PageIndex);

    public static bool ShouldRefreshLive(bool savedSongIsLive, bool liveSectionStillExists)
        => savedSongIsLive && liveSectionStillExists;

    public static bool IsSavedSongLive(long savedSongId, long? liveSongId)
        => savedSongId != 0 && liveSongId == savedSongId;

    public static bool TryMatch(SectionKey live, IReadOnlyList<SectionKey> rebuilt, out int index)
    {
        for (var i = 0; i < rebuilt.Count; i++)
        {
            if (string.Equals(rebuilt[i].SectionType, live.SectionType, StringComparison.OrdinalIgnoreCase)
                && rebuilt[i].SectionOrder == live.SectionOrder
                && rebuilt[i].PageIndex == live.PageIndex)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}
