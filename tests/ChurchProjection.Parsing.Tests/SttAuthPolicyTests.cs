using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SttAuthPolicyTests
{
    [Fact]
    public void Cloud_token_wins_when_the_server_mints()
    {
        Assert.Equal(SttAuthMode.CloudToken, SttAuthPolicy.Resolve("sut_live_abc", "sk_local"));
    }

    [Fact]
    public void Local_key_is_used_when_the_cloud_mint_fails()
    {
        Assert.Equal(SttAuthMode.LocalKey, SttAuthPolicy.Resolve(cloudToken: null, localKey: "sk_local"));
        Assert.Equal(SttAuthMode.LocalKey, SttAuthPolicy.Resolve("", "sk_local"));
    }

    [Fact]
    public void Unavailable_when_neither_cloud_token_nor_local_key_exists()
    {
        Assert.Equal(SttAuthMode.Unavailable, SttAuthPolicy.Resolve(null, null));
        Assert.Equal(SttAuthMode.Unavailable, SttAuthPolicy.Resolve("", "  "));
    }
}
