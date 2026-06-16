using ChurchProjection.Core.Models.Content;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ChurchProjection.Infrastructure.Data;

public class ScriptureRepository
{
    private const string SelectColumns =
        "id, translation, book, chapter, verse_start, verse_end, text, api_bible_id, cached_at, updated_at";

    private readonly DatabaseService _db;

    public ScriptureRepository(DatabaseService db) => _db = db;

    public async Task<ScripturePassage?> FindAsync(string book, int chapter, int verseStart, int? verseEnd, string translation = "BSB")
    {
        await using var conn = _db.GetConnection();

        string sql;
        object param;

        if (verseEnd.HasValue)
        {
            sql = $"""
                SELECT {SelectColumns}
                FROM scriptures
                WHERE book = @book AND chapter = @chapter
                  AND verse_start = @verseStart AND verse_end = @verseEnd
                  AND translation = @translation
                """;
            param = new { book, chapter, verseStart, verseEnd, translation };
        }
        else
        {
            sql = $"""
                SELECT {SelectColumns}
                FROM scriptures
                WHERE book = @book AND chapter = @chapter
                  AND verse_start = @verseStart AND verse_end IS NULL
                  AND translation = @translation
                """;
            param = new { book, chapter, verseStart, translation };
        }

        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, param);
        return row is null ? null : MapRow(row);
    }

    public async Task<List<ScripturePassage>> FindAllInChapterAsync(string book, int chapter, string translation = "BSB")
    {
        await using var conn = _db.GetConnection();
        var rows = await conn.QueryAsync<dynamic>(
            $"""
            SELECT {SelectColumns}
            FROM scriptures
            WHERE book = @book AND chapter = @chapter AND translation = @translation AND verse_end IS NULL
            ORDER BY verse_start
            """,
            new { book, chapter, translation });
        return rows.Select(MapRow).ToList();
    }

    public async Task<List<ScripturePassage>> FindVersesInRangeAsync(string book, int chapter, int verseStart, int? verseEnd, string translation = "BSB")
    {
        await using var conn = _db.GetConnection();
        var end = verseEnd ?? verseStart;
        var rows = await conn.QueryAsync<dynamic>(
            $"""
            SELECT {SelectColumns}
            FROM scriptures
            WHERE book = @book AND chapter = @chapter AND translation = @translation
              AND verse_start >= @verseStart AND verse_start <= @end AND verse_end IS NULL
            ORDER BY verse_start
            """,
            new { book, chapter, translation, verseStart, end });
        return rows.Select(MapRow).ToList();
    }

    public async Task<int> CountVersesInChapterAsync(string book, int chapter, string translation = "BSB")
    {
        await using var conn = _db.GetConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM scriptures WHERE book = @book AND chapter = @chapter AND translation = @translation AND verse_end IS NULL",
            new { book, chapter, translation });
    }

    public async Task BulkInsertAsync(IEnumerable<ScripturePassage> passages)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await using var tx = conn.BeginTransaction();
            foreach (var p in passages)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT OR IGNORE INTO scriptures (translation, book, chapter, verse_start, verse_end, text)
                    VALUES (@Translation, @Book, @Chapter, @VerseStart, @VerseEnd, @Text)
                    """,
                    p, tx);
            }
            tx.Commit();
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    /// <summary>Keyword/phrase search over cached verses, scoped to a single translation so results
    /// never mix versions (e.g. an NIV search must not surface KJV/BSB rows).</summary>
    public async Task<List<ScripturePassage>> SearchAsync(string query, string translation = "BSB")
    {
        await using var conn = _db.GetConnection();
        var pattern = $"%{query}%";
        var rows = await conn.QueryAsync<dynamic>(
            $"""
            SELECT {SelectColumns}
            FROM scriptures
            WHERE translation = @translation AND (text LIKE @pattern OR book LIKE @pattern)
            ORDER BY book, chapter, verse_start
            LIMIT 50
            """,
            new { pattern, translation });

        return rows.Select(MapRow).ToList();
    }

    /// <summary>All single verses for a translation, in canonical-ish order (book, chapter, verse).
    /// Used to build the semantic search index. Ordering is stable so a saved embedding file can be
    /// zipped back to verse text on reload.</summary>
    public async Task<List<ScripturePassage>> GetAllVersesAsync(string translation)
    {
        await using var conn = _db.GetConnection();
        var rows = await conn.QueryAsync<dynamic>(
            $"""
            SELECT {SelectColumns}
            FROM scriptures
            WHERE translation = @translation AND verse_end IS NULL
            ORDER BY book, chapter, verse_start
            """,
            new { translation });
        return rows.Select(MapRow).ToList();
    }

    /// <summary>Total number of single verses cached for a translation. Used to detect when the
    /// semantic index has gone stale because more of the Bible has since downloaded.</summary>
    public async Task<int> CountVersesAsync(string translation)
    {
        await using var conn = _db.GetConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM scriptures WHERE translation = @translation AND verse_end IS NULL",
            new { translation });
    }

    /// <summary>Keyword search scoped to a translation, ranked by how many of the supplied tokens a
    /// verse contains. Used as the keyword half of topical search.</summary>
    public async Task<List<ScripturePassage>> SearchByKeywordsAsync(IReadOnlyList<string> tokens, string translation, int limit)
    {
        if (tokens.Count == 0) return [];

        await using var conn = _db.GetConnection();

        var parameters = new DynamicParameters();
        parameters.Add("translation", translation);
        parameters.Add("limit", limit);

        var scoreParts = new List<string>();
        var whereParts = new List<string>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var name = $"t{i}";
            parameters.Add(name, $"%{tokens[i]}%");
            scoreParts.Add($"(CASE WHEN text LIKE @{name} THEN 1 ELSE 0 END)");
            whereParts.Add($"text LIKE @{name}");
        }

        var score = string.Join(" + ", scoreParts);
        var where = string.Join(" OR ", whereParts);

        var sql = $"""
            SELECT {SelectColumns}, ({score}) AS match_score
            FROM scriptures
            WHERE translation = @translation AND verse_end IS NULL AND ({where})
            ORDER BY match_score DESC, book, chapter, verse_start
            LIMIT @limit
            """;

        var rows = await conn.QueryAsync<dynamic>(sql, parameters);
        return rows.Select(MapRow).ToList();
    }

    public async Task<ScripturePassage> UpsertAsync(ScripturePassage passage)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await UpsertCoreAsync(conn, null, passage).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
        return passage;
    }

    public async Task UpsertManyAsync(IEnumerable<ScripturePassage> passages, CancellationToken cancellationToken = default)
    {
        await _db.WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await using var tx = conn.BeginTransaction();
            foreach (var passage in passages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UpsertCoreAsync(conn, tx, passage).ConfigureAwait(false);
            }
            tx.Commit();
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    private static async Task UpsertCoreAsync(SqliteConnection conn, SqliteTransaction? tx, ScripturePassage passage)
    {
        var updated = await conn.ExecuteAsync(
            """
            UPDATE scriptures SET text = @Text, updated_at = datetime('now')
            WHERE translation = @Translation AND book = @Book AND chapter = @Chapter
              AND verse_start = @VerseStart AND (verse_end = @VerseEnd OR (verse_end IS NULL AND @VerseEnd IS NULL))
            """,
            passage, tx);

        if (updated == 0)
        {
            passage.Id = await conn.QuerySingleAsync<long>(
                """
                INSERT INTO scriptures (translation, book, chapter, verse_start, verse_end, text, api_bible_id)
                VALUES (@Translation, @Book, @Chapter, @VerseStart, @VerseEnd, @Text, @ApiBibleId);
                SELECT last_insert_rowid();
                """,
                passage, tx);
        }
    }

    private static ScripturePassage MapRow(dynamic row) => new()
    {
        Id = (long)row.id,
        Translation = (string)row.translation,
        Book = (string)row.book,
        Chapter = (int)(long)row.chapter,
        VerseStart = (int)(long)row.verse_start,
        VerseEnd = row.verse_end is null ? null : (int?)(long)row.verse_end,
        Text = (string)row.text,
        ApiBibleId = row.api_bible_id as string,
    };
}
