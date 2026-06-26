using ChurchProjection.Core.Models.Content;
using ChurchProjection.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// Behaviour contract for the notes library (prayer points etc.): notes can be saved, listed
/// (most-recent first), edited and deleted, all persisted in the local SQLite database.
/// </summary>
public class NotesRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cp-notes-{Guid.NewGuid():N}.db");
    private readonly DatabaseService _db;
    private readonly NotesRepository _repo;

    public NotesRepositoryTests()
    {
        _db = new DatabaseService(_dbPath);
        _db.InitializeAsync().GetAwaiter().GetResult();
        _repo = new NotesRepository(_db);
    }

    [Fact]
    public async Task Insert_assigns_an_id_and_is_returned_by_GetAll()
    {
        var saved = await _repo.InsertAsync(new Note { Title = "Prayer Points", Body = "For the nation\nFor the sick" });

        Assert.True(saved.Id > 0);

        var all = await _repo.GetAllAsync();
        var loaded = Assert.Single(all);
        Assert.Equal("Prayer Points", loaded.Title);
        Assert.Equal("For the nation\nFor the sick", loaded.Body);
    }

    [Fact]
    public async Task GetAll_returns_notes_most_recently_updated_first()
    {
        var first = await _repo.InsertAsync(new Note { Title = "First", Body = "a", UpdatedAt = DateTime.UtcNow.AddMinutes(-10) });
        var second = await _repo.InsertAsync(new Note { Title = "Second", Body = "b", UpdatedAt = DateTime.UtcNow });

        var all = await _repo.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("Second", all[0].Title);
        Assert.Equal("First", all[1].Title);
    }

    [Fact]
    public async Task Update_changes_title_and_body()
    {
        var note = await _repo.InsertAsync(new Note { Title = "Old", Body = "old body" });

        note.Title = "New";
        note.Body = "new body";
        await _repo.UpdateAsync(note);

        var loaded = Assert.Single(await _repo.GetAllAsync());
        Assert.Equal("New", loaded.Title);
        Assert.Equal("new body", loaded.Body);
    }

    [Fact]
    public async Task Delete_removes_the_note()
    {
        var note = await _repo.InsertAsync(new Note { Title = "Temp", Body = "x" });

        await _repo.DeleteAsync(note.Id);

        Assert.Empty(await _repo.GetAllAsync());
    }

    [Fact]
    public async Task Delete_of_a_missing_id_is_a_no_op()
    {
        await _repo.InsertAsync(new Note { Title = "Keep", Body = "x" });

        await _repo.DeleteAsync(99999);

        Assert.Single(await _repo.GetAllAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch { /* best effort */ }
        }
    }
}
