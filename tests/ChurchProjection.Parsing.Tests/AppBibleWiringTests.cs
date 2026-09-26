using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class AppBibleWiringTests
{
    [Fact]
    public void App_does_not_construct_ApiBibleClient()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "ChurchProjection.App", "App.axaml.cs"));
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("new ApiBibleClient", source);
    }
}
