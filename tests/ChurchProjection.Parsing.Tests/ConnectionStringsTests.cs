using ChurchProjection.Api;
using Npgsql;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class ConnectionStringsTests
{
    [Fact]
    public void Normalize_neon_uri_with_channel_binding_does_not_throw()
    {
        const string uri =
            "postgresql://neondb_owner:s3cret@ep-example-pooler.c-4.us-east-2.aws.neon.tech/neondb?sslmode=require&channel_binding=require";

        var normalized = ConnectionStrings.Normalize(uri);
        var parsed = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("ep-example-pooler.c-4.us-east-2.aws.neon.tech", parsed.Host);
        Assert.Equal("neondb", parsed.Database);
        Assert.Equal("neondb_owner", parsed.Username);
        Assert.Equal("s3cret", parsed.Password);
        Assert.Equal(SslMode.Require, parsed.SslMode);
        Assert.Equal(ChannelBinding.Prefer, parsed.ChannelBinding);
        Assert.DoesNotContain("postgresql://", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_leaves_key_value_strings_alone()
    {
        const string kv = "Host=localhost;Username=u;Password=p;Database=d";
        Assert.Equal(kv, ConnectionStrings.Normalize(kv));
    }
}
