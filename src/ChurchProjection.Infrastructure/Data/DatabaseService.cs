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

        await conn.ExecuteAsync(
            "UPDATE songs SET organization_id = 'local-default' WHERE organization_id IS NULL OR organization_id = ''")
            .ConfigureAwait(false);
        await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_org ON songs(organization_id)").ConfigureAwait(false);
        await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_dirty ON songs(organization_id, dirty)").ConfigureAwait(false);
        await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_songs_cloud ON songs(cloud_id)").ConfigureAwait(false);

        Log.Information("SQLite database initialized");
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
