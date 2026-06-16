using ChurchProjection.Core.Models.Content;

namespace ChurchProjection.Core.Services;

public interface IContentLibraryService
{
    Task<List<ScripturePassage>> SearchScripturesAsync(string query, string translation = "BSB", CancellationToken cancellationToken = default);
    Task<ScripturePassage?> GetOrFetchScriptureAsync(ScriptureReference reference, string translation = "BSB", CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns verses for the reference from the local cache only when <paramref name="localOnly"/>
    /// is true (used on the live matching hot path to avoid any network I/O). Otherwise falls back
    /// to the Bible API and persists the result.
    /// </summary>
    Task<List<ScripturePassage>> GetOrFetchVersesAsync(ScriptureReference reference, string translation = "BSB", bool localOnly = false, CancellationToken cancellationToken = default);
    Task<List<Song>> GetAllSongsAsync(CancellationToken cancellationToken = default);
    Task<List<Song>> SearchSongsAsync(string query, CancellationToken cancellationToken = default);
    Task<Song> ImportSongAsync(string title, string rawLyrics, string? artist = null, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new song (Id == 0) or updates an existing one, including its edited sections.</summary>
    Task<Song> SaveSongAsync(Song song, CancellationToken cancellationToken = default);

    Task DeleteSongAsync(long songId, CancellationToken cancellationToken = default);
}
