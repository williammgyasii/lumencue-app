namespace ChurchProjection.Infrastructure.Bible;

/// <summary>
/// Shared, long-lived <see cref="HttpClient"/> instances per Bible host. Reused across services
/// to avoid socket exhaustion. The timeout is generous to allow bulk downloads; latency-sensitive
/// callers apply their own shorter timeout via a linked <see cref="CancellationTokenSource"/>.
/// </summary>
internal static class BibleHttpClients
{
    public static readonly HttpClient Helloao = new()
    {
        BaseAddress = new Uri("https://bible.helloao.org/api/"),
        Timeout = TimeSpan.FromMinutes(10),
    };
}
