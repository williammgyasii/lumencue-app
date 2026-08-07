using Npgsql;

namespace ChurchProjection.Api;

internal static class ConnectionStrings
{
    /// <summary>
    /// Render/Fly inject postgres:// URIs; RDS/local use key=value. Npgsql accepts both once normalized.
    /// </summary>
    internal static string Normalize(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var csb = new NpgsqlConnectionStringBuilder
        {
            ConnectionString = connectionString,
        };

        if (connectionString.Contains("rds.amazonaws.com", StringComparison.OrdinalIgnoreCase))
        {
            csb.SslMode = SslMode.Require;
            csb.TrustServerCertificate = true;
        }

        return csb.ConnectionString;
    }
}
