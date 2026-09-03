using System.Runtime.CompilerServices;
using Npgsql;

[assembly: InternalsVisibleTo("ChurchProjection.Parsing.Tests")]

namespace ChurchProjection.Api;

internal static class ConnectionStrings
{
    /// <summary>
    /// Hosted platforms inject <c>postgres(ql)://</c> URIs (Neon includes
    /// <c>channel_binding=require</c>). Feeding that URI straight into
    /// <see cref="NpgsqlConnectionStringBuilder"/> can throw, which used to
    /// silently fall back to 127.0.0.1. Parse the URI ourselves instead.
    /// </summary>
    internal static string Normalize(string connectionString)
    {
        if (!IsPostgresUri(connectionString))
            return connectionString;

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Database = uri.AbsolutePath.Trim('/'),
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "",
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            SslMode = SslMode.Require,
            // Neon sets channel_binding=require. Prefer still uses SCRAM-SHA-256-PLUS
            // when the server offers it, without rejecting the URI parse.
            ChannelBinding = ChannelBinding.Prefer,
        };

        if (!uri.IsDefaultPort && uri.Port > 0)
            csb.Port = uri.Port;

        if (uri.Host.Contains("rds.amazonaws.com", StringComparison.OrdinalIgnoreCase))
            csb.TrustServerCertificate = true;

        return csb.ConnectionString;
    }

    internal static bool IsPostgresUri(string connectionString) =>
        connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
}
