using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class SemanticEmbeddingPolicyTests
{
    [Fact]
    public void Startup_does_not_load_embeddings()
    {
        Assert.False(SemanticEmbeddingPolicy.LoadAtLaunch);
    }

    [Fact]
    public void Index_after_cache_is_off()
    {
        Assert.False(SemanticEmbeddingPolicy.IndexAfterBibleCache);
    }
}
