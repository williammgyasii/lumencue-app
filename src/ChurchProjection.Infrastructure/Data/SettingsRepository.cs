using Dapper;

namespace ChurchProjection.Infrastructure.Data;

public class SettingsRepository
{
    private readonly DatabaseService _db;

    public SettingsRepository(DatabaseService db) => _db = db;

    public async Task<string?> GetAsync(string key)
    {
        await using var conn = _db.GetConnection();
        return await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT value FROM settings WHERE key = @key", new { key });
    }

    public async Task SetAsync(string key, string value)
    {
        await _db.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                """
                INSERT INTO settings (key, value) VALUES (@key, @value)
                ON CONFLICT(key) DO UPDATE SET value = @value
                """,
                new { key, value });
        }
        finally
        {
            _db.WriteLock.Release();
        }
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var val = await GetAsync(key);
        return val is not null ? val == "true" : defaultValue;
    }

    public Task SetBoolAsync(string key, bool value)
        => SetAsync(key, value ? "true" : "false");
}
