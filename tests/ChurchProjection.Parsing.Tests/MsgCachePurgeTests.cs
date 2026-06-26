using ChurchProjection.Infrastructure.Data;
using Dapper;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// MSG chapters cached before the grouped-verse fix were stored clamped, leaving individual verses
/// unreachable. A one-time startup migration purges the stale MSG rows so the translation re-fetches
/// broken down. These tests pin that the purge removes only MSG, runs once, and is idempotent.
/// </summary>
public class MsgCachePurgeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cp-purge-{Guid.NewGuid():N}.db");

    private DatabaseService NewDb() => new(_dbPath);

    private void SeedStaleMsgData()
    {
        using var db = NewDb();
        using var conn = db.GetConnection();

        // Pretend the migration never ran on this machine.
        conn.Execute("DELETE FROM settings WHERE key = 'migration_msg_verse_split_v1'");

        conn.Execute(
            "INSERT INTO scriptures (translation, book, chapter, verse_start, verse_end, text) VALUES (@t, @b, @c, @v, NULL, @x)",
            new[]
            {
                new { t = "MSG", b = "Psalms", c = 109, v = 1, x = "clamped block" },
                new { t = "MSG", b = "John", c = 3, v = 16, x = "msg john" },
            });
        conn.Execute(
            "INSERT INTO scriptures (translation, book, chapter, verse_start, verse_end, text) VALUES ('KJV', 'John', 3, 16, NULL, 'For God so loved...')");
        conn.Execute(
            "INSERT OR REPLACE INTO bible_cache_status (translation, total_chapters, cached_chapters, is_complete) VALUES ('MSG', 1189, 1189, 1)");
    }

    private (int Msg, int Kjv, int MsgStatus) Counts()
    {
        using var db = NewDb();
        using var conn = db.GetConnection();
        var msg = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM scriptures WHERE translation = 'MSG'");
        var kjv = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM scriptures WHERE translation = 'KJV'");
        var status = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM bible_cache_status WHERE translation = 'MSG'");
        return (msg, kjv, status);
    }

    [Fact]
    public async Task Purges_stale_msg_rows_but_keeps_other_translations()
    {
        // First init creates the schema (and sets the marker on a fresh db).
        using (var db = NewDb()) await db.InitializeAsync();

        SeedStaleMsgData();

        // Re-init simulates the next app launch: the guarded purge should fire.
        using (var db = NewDb()) await db.InitializeAsync();

        var (msg, kjv, status) = Counts();
        Assert.Equal(0, msg);       // stale MSG verses dropped
        Assert.Equal(1, kjv);       // other translations untouched
        Assert.Equal(0, status);    // MSG cache-status cleared so it re-downloads
    }

    [Fact]
    public async Task Does_not_purge_again_on_subsequent_launches()
    {
        using (var db = NewDb()) await db.InitializeAsync();
        SeedStaleMsgData();
        using (var db = NewDb()) await db.InitializeAsync();   // purge runs, marker set

        // A fresh MSG fetch repopulates rows after the migration…
        using (var db = NewDb())
        {
            using var conn = db.GetConnection();
            conn.Execute(
                "INSERT INTO scriptures (translation, book, chapter, verse_start, verse_end, text) VALUES ('MSG', 'Psalms', 109, 2, NULL, 'fresh verse')");
        }

        // …and a later launch must NOT wipe them again.
        using (var db = NewDb()) await db.InitializeAsync();

        var (msg, _, _) = Counts();
        Assert.Equal(1, msg);
    }

    public void Dispose()
    {
        SqliteConnectionCleanup();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch { /* best effort */ }
        }
    }

    private void SqliteConnectionCleanup()
    {
        // Release pooled handles so the temp file can be deleted on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
