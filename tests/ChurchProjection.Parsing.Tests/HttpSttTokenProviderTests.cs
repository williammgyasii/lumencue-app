using System.Net;
using System.Text;
using ChurchProjection.Infrastructure.Audio;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

/// <summary>
/// ElevenLabs single-use Scribe tokens are consumed on the first WebSocket connect.
/// The provider must mint a new one on every GetTokenAsync (reconnects included).
/// Caching a Deepgram-style JWT would reuse a dead token and fail the next connect.
/// </summary>
public class HttpSttTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_DoesNotReuseAPreviousToken()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var provider = new HttpSttTokenProvider(http);

        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync();

        Assert.Equal("tok-1", first);
        Assert.Equal("tok-2", second);
        Assert.Equal(2, handler.Calls);
        Assert.All(handler.Paths, p => Assert.Equal("/stt/token", p));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Paths.Add(request.RequestUri?.AbsolutePath ?? "");
            var json = $$"""{"accessToken":"tok-{{Calls}}","expiresIn":900}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
