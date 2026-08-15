using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace ChurchProjection.Infrastructure.Data;

/// <summary>
/// Owns SQLite access. Hands out short-lived pooled connections (callers must dispose) so the
/// live matching read path can run concurrently with background writes. The database is opened
/// in WAL mode for concurrent readers/writer; writes are serialized via <see cref="WriteLock"/>.
/// </summary>
public sealed class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public DatabaseService(string dbPath = "churchprojection.db")
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = true,
            DefaultTimeout = 30,
        }.ToString();
    }

    /// <summary>Returns a freshly opened pooled connection. Callers must dispose it (use <c>using</c>).</summary>
    public SqliteConnection GetConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public SemaphoreSlim WriteLock => _writeLock;

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        Log.Information("Initializing SQLite database...");

        await conn.ExecuteAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;").ConfigureAwait(false);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS scriptures (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                translation TEXT NOT NULL,
                book TEXT NOT NULL,
                chapter INTEGER NOT NULL,
                verse_start INTEGER NOT NULL,
                verse_end INTEGER,
                text TEXT NOT NULL,
                api_bible_id TEXT,
                cached_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS songs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                artist TEXT,
                ccli_number TEXT,
                copyright_info TEXT,
                tags TEXT,
                lines_per_slide INTEGER NOT NULL DEFAULT 0,
                organization_id TEXT NOT NULL DEFAULT 'local-default',
                deleted INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS song_sections (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                song_id INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE,
                section_type TEXT NOT NULL,
                section_order INTEGER NOT NULL,
                text TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS bible_cache_status (
                translation TEXT PRIMARY KEY,
                total_chapters INTEGER NOT NULL DEFAULT 0,
                cached_chapters INTEGER NOT NULL DEFAULT 0,
                is_complete INTEGER NOT NULL DEFAULT 0,
                started_at TEXT,
                completed_at TEXT
            );

            CREATE TABLE IF NOT EXISTS notes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL DEFAULT '',
                body TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_scriptures_ref
                ON scriptures(translation, book, chapter, verse_start);

            CREATE INDEX IF NOT EXISTS idx_scriptures_chapter
                ON scriptures(translation, book, chapter);

            CREATE INDEX IF NOT EXISTS idx_songs_title
                ON songs(title);
            """).ConfigureAwait(false);

        await EnsureColumnAsync(conn, "songs", "lines_per_slide", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);

        // Multi-tenancy: scope songs by organization and support soft-delete tombstones for sync.
        await EnsureColumnAsync(conn, "songs", "organization_id", "TEXT NOT NULL DEFAULT 'local-default'").ConfigureAwait(false);
        await EnsureColumnAsync(conn, "songs", "deleted", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);

        // Cloud sync: cloud_id maps a local row to its Neon uuid; dirty marks rows awaiting push.
        await EnsureColumnAsync(conn, "songs", "cloud_id", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync(conn, "songs", "dirty", "INTEGER NOT NULL DEFAULT 1").ConfigureAwait(false);

        await EnsureColumnAsync(conn, "notes", "split_mode", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await EnsureColumnAsync(conn, "notes", "lines_per_slide", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);

        await conn.ExecuteAsync(
            "UPDATE songs SET organization_id = 'local-default' WHERE organization_id IS NULL OR organization_id = ''")
            .ConfigureAwait(false);
        await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_org ON songs(organization_id)").ConfigureAwait(false);
        await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_dirty ON songs(organization_id, dirty)").ConfigureAwait(false);
        await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_cloud ON songs(cloud_id)").ConfigureAwait(false);

        await PurgeStaleMsgCacheOnceAsync(conn).ConfigureAwait(false);

        Log.Information("SQLite database initialized");
    }

    /// <summary>
    /// One-time cleanup for MSG. Chapters cached before the grouped-verse fix were stored "clamped"
    /// (a span like "[1-3]" was skipped, so individual verses were unreachable and a lookup fell back
    /// to the whole chapter). This drops the stale MSG rows once — and its cache-status marker — so the
    /// translation re-fetches/re-downloads broken down into one row per verse. Guarded by a settings
    /// marker so it runs a single time per machine.
    /// </summary>
    public static async Task PurgeStaleMsgCacheOnceAsync(SqliteConnection conn)
    {
        const string marker = "migration_msg_verse_split_v1";

        var alreadyRun = await conn.ExecuteScalarAsync<string?>(
            "SELECT value FROM settings WHERE key = @marker", new { marker }).ConfigureAwait(false);
        if (alreadyRun is not null) return;

        var removed = await conn.ExecuteAsync(
            "DELETE FROM scriptures WHERE translation = 'MSG'").ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM bible_cache_status WHERE translation = 'MSG'").ConfigureAwait(false);
        await conn.ExecuteAsync(
            "INSERT OR REPLACE INTO settings (key, value) VALUES (@marker, datetime('now'))",
            new { marker }).ConfigureAwait(false);

        Log.Information("Purged {Rows} stale MSG verse(s) so the translation re-caches broken down", removed);
    }

    /// <summary>Adds a column to an existing table if it is not already present (simple migration).</summary>
    private static async Task EnsureColumnAsync(SqliteConnection conn, string table, string column, string definition)
    {
        var columns = await conn.QueryAsync<string>($"SELECT name FROM pragma_table_info('{table}')").ConfigureAwait(false);
        if (!columns.Contains(column, StringComparer.OrdinalIgnoreCase))
            await conn.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}").ConfigureAwait(false);
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
    }
}
