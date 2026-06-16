using System.Net.Http.Headers;
using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>Attaches the current seat token as a Bearer credential to outgoing cloud-API requests.</summary>
public sealed class SeatAuthHandler : DelegatingHandler
{
    private readonly ISeatTokenProvider _tokens;

    public SeatAuthHandler(ISeatTokenProvider tokens, HttpMessageHandler inner)
    {
        _tokens = tokens;
        InnerHandler = inner;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _tokens.CurrentToken;
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
