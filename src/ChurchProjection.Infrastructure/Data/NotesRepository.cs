using ChurchProjection.Core.Models.Content;
using Dapper;

namespace ChurchProjection.Infrastructure.Data;

/// <summary>
/// CRUD for the operator's saved notes (prayer points etc.), backed by the <c>notes</c> table.
/// </summary>
public class NotesRepository
{
    private const string SelectColumns = "id, title, body, split_mode, created_at, updated_at";

    private readonly DatabaseService _db;

    public NotesRepository(DatabaseService db) => _db = db;

    /// <summary>All saved notes, most recently updated first.</summary>
    public async Task<List<Note>> GetAllAsync()
    {
        await using var conn = _db.GetConnection();
        var rows = await conn.QueryAsync<dynamic>(
            $"SELECT {SelectColumns} FROM notes ORDER BY updated_at DESC, id DESC").ConfigureAwait(false);
        return rows.Select(MapRow).ToList();
    }

    /// <summary>Inserts a new note and returns it with its assigned id.</summary>
    public async Task<Note> InsertAsync(Note note)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            note.Id = await conn.QuerySingleAsync<long>(
                """
                INSERT INTO notes (title, body, split_mode, created_at, updated_at)
                VALUES (@Title, @Body, @SplitMode, @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();
                """,
                new
                {
                    note.Title,
                    note.Body,
                    SplitMode = (int)note.SplitMode,
                    CreatedAt = note.CreatedAt.ToString("o"),
                    UpdatedAt = note.UpdatedAt.ToString("o"),
                }).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
        return note;
    }

    /// <summary>Updates an existing note's title/body and bumps its updated timestamp.</summary>
    public async Task UpdateAsync(Note note)
    {
        note.UpdatedAt = DateTime.UtcNow;
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                "UPDATE notes SET title = @Title, body = @Body, split_mode = @SplitMode, updated_at = @UpdatedAt WHERE id = @Id",
                new { note.Title, note.Body, SplitMode = (int)note.SplitMode, UpdatedAt = note.UpdatedAt.ToString("o"), note.Id })
                .ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    /// <summary>Deletes the note with the given id (no-op if it doesn't exist).</summary>
    public async Task DeleteAsync(long id)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync("DELETE FROM notes WHERE id = @id", new { id }).ConfigureAwait(false);
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    private static Note MapRow(dynamic row) => new()
    {
        Id = (long)row.id,
        Title = (string)row.title,
        Body = (string)row.body,
        SplitMode = row.split_mode is null ? NoteSplitMode.AutoFit : (NoteSplitMode)(int)row.split_mode,
        CreatedAt = ParseDate(row.created_at as string),
        UpdatedAt = ParseDate(row.updated_at as string),
    };

    private static DateTime ParseDate(string? raw) =>
        DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : DateTime.UtcNow;
}
