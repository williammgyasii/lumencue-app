using Dapper;
using Npgsql;

namespace ChurchProjection.Api;

/// <summary>
/// Readiness report for operators. Always returns JSON — never an empty 500 —
/// so a missing schema or a bad Neon URL is visible instead of silent.
/// </summary>
public static class Health
{
    public static readonly string[] RequiredTables =
    [
        "schema_meta",
        "organizations",
        "branches",
        "plans",
        "subscriptions",
        "entitlements",
        "billing_events",
        "seats",
        "device_activations",
        "stt_usage",
        "songs",
    ];

    public static async Task<IResult> CheckAsync(NpgsqlDataSource dataSource, HealthHints hints)
    {
        var tables = RequiredTables.ToDictionary(name => name, _ => false, StringComparer.Ordinal);
        DateTime? dbTime = null;
        int? schemaVersion = null;
        string? error = hints.ConfigError;

        try
        {
            await using var conn = await dataSource.OpenConnectionAsync();
            dbTime = await conn.ExecuteScalarAsync<DateTime>("select now()");

            var existing = (await conn.QueryAsync<string>(
                """
                select tablename
                from pg_tables
                where schemaname = 'public'
                """)).ToHashSet(StringComparer.Ordinal);

            foreach (var name in RequiredTables)
                tables[name] = existing.Contains(name);

            if (tables["schema_meta"])
                schemaVersion = await conn.ExecuteScalarAsync<int?>("select version from schema_meta limit 1");
        }
        catch (Exception ex)
        {
            error = SafeError(ex);
        }

        var missing = tables.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        var ok = hints.NeonConfigured
                 && error is null
                 && schemaVersion >= 2
                 && missing.Count == 0;

        var body = new
        {
            ok,
            service = "LumenCue Cloud API",
            neonConfigured = hints.NeonConfigured,
            database = new
            {
                reachable = dbTime is not null,
                time = dbTime,
                schemaVersion,
                expectedSchemaVersion = 2,
                tables,
                missingTables = missing,
                error,
            },
        };

        return ok ? Results.Ok(body) : Results.Json(body, statusCode: 503);
    }

    internal static string SafeError(Exception ex)
    {
        // Never echo a connection string or password if Npgsql put one in the message.
        var message = ex.Message;
        if (message.Contains("postgres", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("password", StringComparison.OrdinalIgnoreCase))
            return $"{ex.GetType().Name}: database connection failed";
        return $"{ex.GetType().Name}: {message}";
    }
}

public sealed record HealthHints(bool NeonConfigured, string? ConfigError);
