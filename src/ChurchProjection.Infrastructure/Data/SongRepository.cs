using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ChurchProjection.Infrastructure.Data;

public class SongRepository
{
    private readonly DatabaseService _db;
    private readonly ITenantContext _tenant;

    public SongRepository(DatabaseService db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Raised after a local user edit (insert/update/delete) so the sync scheduler can push promptly.</summary>
    public event Action? Changed;

    public async Task<List<Song>> GetAllAsync()
    {
        await using var conn = _db.GetConnection();
        var songs = (await conn.QueryAsync<Song>(
            """
            SELECT id Id, title Title, artist Artist, ccli_number CcliNumber, tags Tags, lines_per_slide LinesPerSlide,
                   organization_id OrganizationId, created_at CreatedAt, updated_at UpdatedAt
            FROM songs
            WHERE organization_id = @org AND deleted = 0
            ORDER BY title
            """,
            new { org = _tenant.OrganizationId }))
            .ToList();

        foreach (var song in songs)
            song.Sections = await GetSectionsAsync(conn, song.Id);

        return songs;
    }

    public async Task<List<Song>> SearchAsync(string query)
    {
        await using var conn = _db.GetConnection();
        var pattern = $"%{query}%";
        var songs = (await conn.QueryAsync<Song>(
            """
            SELECT id Id, title Title, artist Artist, ccli_number CcliNumber, tags Tags, lines_per_slide LinesPerSlide,
                   organization_id OrganizationId, created_at CreatedAt, updated_at UpdatedAt
            FROM songs
            WHERE organization_id = @org AND deleted = 0
              AND (title LIKE @pattern OR artist LIKE @pattern OR tags LIKE @pattern)
            ORDER BY title
            LIMIT 50
            """,
            new { pattern, org = _tenant.OrganizationId }))
            .ToList();

        foreach (var song in songs)
            song.Sections = await GetSectionsAsync(conn, song.Id);

        return songs;
    }

    public async Task<Song> InsertAsync(Song song)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await using var tx = conn.BeginTransaction();

            song.OrganizationId = _tenant.OrganizationId;
            song.Id = await conn.QuerySingleAsync<long>(
                """
                INSERT INTO songs (title, artist, ccli_number, copyright_info, tags, lines_per_slide, organization_id, deleted, dirty, updated_at)
                VALUES (@Title, @Artist, @CcliNumber, @CopyrightInfo, @Tags, @LinesPerSlide, @OrganizationId, 0, 1, datetime('now'));
                SELECT last_insert_rowid();
                """,
                song, tx);

            foreach (var section in song.Sections)
            {
                section.SongId = song.Id;
                section.Id = await conn.QuerySingleAsync<long>(
                    """
                    INSERT INTO song_sections (song_id, section_type, section_order, text)
                    VALUES (@SongId, @SectionType, @SectionOrder, @Text);
                    SELECT last_insert_rowid();
                    """,
                    section, tx);
            }

            tx.Commit();
            Changed?.Invoke();
            return song;
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    public async Task<Song> UpdateAsync(Song song)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await using var tx = conn.BeginTransaction();

            song.OrganizationId = _tenant.OrganizationId;
            await conn.ExecuteAsync(
                """
                UPDATE songs
                SET title = @Title, artist = @Artist, ccli_number = @CcliNumber,
                    copyright_info = @CopyrightInfo, tags = @Tags, lines_per_slide = @LinesPerSlide,
                    dirty = 1, updated_at = datetime('now')
                WHERE id = @Id AND organization_id = @OrganizationId;
                """,
                song, tx);

            // Replace sections wholesale — simplest correct way to reflect adds/removes/reorders.
            await conn.ExecuteAsync("DELETE FROM song_sections WHERE song_id = @Id", new { song.Id }, tx);

            foreach (var section in song.Sections)
            {
                section.SongId = song.Id;
                section.Id = await conn.QuerySingleAsync<long>(
                    """
                    INSERT INTO song_sections (song_id, section_type, section_order, text)
                    VALUES (@SongId, @SectionType, @SectionOrder, @Text);
                    SELECT last_insert_rowid();
                    """,
                    section, tx);
            }

            tx.Commit();
            Changed?.Invoke();
            return song;
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    public async Task DeleteAsync(long songId)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            // Soft-delete (tombstone) so the deletion can propagate to the cloud on the next sync.
            await conn.ExecuteAsync(
                "UPDATE songs SET deleted = 1, dirty = 1, updated_at = datetime('now') WHERE id = @songId AND organization_id = @org",
                new { songId, org = _tenant.OrganizationId });
        }
        finally
        {
            _db.WriteLock.Release();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// On first sign-in, re-stamps the local default library to the signed-in organization so a
    /// church's existing songs are carried over (and will sync up under their org).
    /// </summary>
    public async Task AdoptDefaultLibraryAsync(string organizationId)
    {
        if (string.IsNullOrWhiteSpace(organizationId) || organizationId == ITenantContext.DefaultOrganizationId)
            return;

        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                "UPDATE songs SET organization_id = @org, dirty = 1, updated_at = datetime('now') WHERE organization_id = @default",
                new { org = organizationId, @default = ITenantContext.DefaultOrganizationId });
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    // ---- Cloud sync -----------------------------------------------------

    /// <summary>Local rows for the active org awaiting push (dirty), including tombstones.</summary>
    public async Task<List<Song>> GetPendingPushAsync()
    {
        await using var conn = _db.GetConnection();
        var songs = (await conn.QueryAsync<Song>(
            """
            SELECT id Id, title Title, artist Artist, ccli_number CcliNumber, copyright_info CopyrightInfo,
                   tags Tags, lines_per_slide LinesPerSlide, organization_id OrganizationId,
                   cloud_id CloudId, deleted Deleted, created_at CreatedAt, updated_at UpdatedAt
            FROM songs
            WHERE organization_id = @org AND dirty = 1
            """,
            new { org = _tenant.OrganizationId }))
            .ToList();

        foreach (var song in songs)
            song.Sections = await GetSectionsAsync(conn, song.Id);

        return songs;
    }

    /// <summary>Persists the cloud uuid assigned to a local row before its first push.</summary>
    public async Task SetCloudIdAsync(long localId, string cloudId)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                "UPDATE songs SET cloud_id = @cloudId WHERE id = @localId",
                new { cloudId, localId });
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    /// <summary>Clears the dirty flag for rows that were successfully pushed.</summary>
    public async Task MarkPushedAsync(IEnumerable<long> localIds)
    {
        var ids = localIds.ToList();
        if (ids.Count == 0) return;

        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync("UPDATE songs SET dirty = 0 WHERE id IN @ids", new { ids });
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    /// <summary>
    /// Applies cloud songs into the local store (upsert by cloud_id). Last-write-wins: a locally-dirty
    /// row is left alone (its pending edit will be pushed and win), so user work is never clobbered.
    /// </summary>
    public async Task<int> ApplyCloudAsync(IReadOnlyList<Song> cloudSongs)
    {
        if (cloudSongs.Count == 0) return 0;

        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await using var tx = conn.BeginTransaction();
            var applied = 0;

            foreach (var cloud in cloudSongs)
            {
                if (string.IsNullOrWhiteSpace(cloud.CloudId)) continue;

                var local = await conn.QueryFirstOrDefaultAsync<(long Id, long Dirty)?>(
                    "SELECT id Id, dirty Dirty FROM songs WHERE cloud_id = @cid",
                    new { cid = cloud.CloudId }, tx);

                var stamp = cloud.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
                var org = string.IsNullOrWhiteSpace(cloud.OrganizationId) ? _tenant.OrganizationId : cloud.OrganizationId;

                if (local is { } row)
                {
                    if (row.Dirty == 1) continue; // local edit pending: it wins, skip overwrite

                    await conn.ExecuteAsync(
                        """
                        UPDATE songs
                        SET title = @Title, artist = @Artist, ccli_number = @CcliNumber, copyright_info = @CopyrightInfo,
                            tags = @Tags, lines_per_slide = @LinesPerSlide, organization_id = @Org,
                            deleted = @Deleted, dirty = 0, updated_at = @Stamp
                        WHERE id = @Id
                        """,
                        new
                        {
                            cloud.Title, cloud.Artist, cloud.CcliNumber, cloud.CopyrightInfo, cloud.Tags,
                            cloud.LinesPerSlide, Org = org, Deleted = cloud.Deleted ? 1 : 0, Stamp = stamp, row.Id,
                        }, tx);

                    await ReplaceSectionsAsync(conn, tx, row.Id, cloud.Sections);
                    applied++;
                }
                else
                {
                    if (cloud.Deleted) continue; // nothing local to tombstone

                    var newId = await conn.QuerySingleAsync<long>(
                        """
                        INSERT INTO songs (title, artist, ccli_number, copyright_info, tags, lines_per_slide,
                                           organization_id, cloud_id, deleted, dirty, created_at, updated_at)
                        VALUES (@Title, @Artist, @CcliNumber, @CopyrightInfo, @Tags, @LinesPerSlide,
                                @Org, @CloudId, 0, 0, @Stamp, @Stamp);
                        SELECT last_insert_rowid();
                        """,
                        new
                        {
                            cloud.Title, cloud.Artist, cloud.CcliNumber, cloud.CopyrightInfo, cloud.Tags,
                            cloud.LinesPerSlide, Org = org, cloud.CloudId, Stamp = stamp,
                        }, tx);

                    await ReplaceSectionsAsync(conn, tx, newId, cloud.Sections);
                    applied++;
                }
            }

            tx.Commit();
            if (applied > 0) Changed?.Invoke();
            return applied;
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    private static async Task ReplaceSectionsAsync(SqliteConnection conn, SqliteTransaction tx, long songId, List<SongSection> sections)
    {
        await conn.ExecuteAsync("DELETE FROM song_sections WHERE song_id = @songId", new { songId }, tx);
        foreach (var section in sections)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO song_sections (song_id, section_type, section_order, text)
                VALUES (@SongId, @SectionType, @SectionOrder, @Text)
                """,
                new { SongId = songId, section.SectionType, section.SectionOrder, section.Text }, tx);
        }
    }

    private static async Task<List<SongSection>> GetSectionsAsync(SqliteConnection conn, long songId)
    {
        return (await conn.QueryAsync<SongSection>(
            """
            SELECT id Id, song_id SongId, section_type SectionType, section_order SectionOrder, text Text
            FROM song_sections WHERE song_id = @songId ORDER BY section_order
            """,
            new { songId }))
            .ToList();
    }
}
