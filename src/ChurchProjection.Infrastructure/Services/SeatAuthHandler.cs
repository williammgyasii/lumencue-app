using System.Net.Http.Headers;
using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Attaches the current seat token (Bearer) and the machine's hardware fingerprint
/// (<c>X-Hardware-Id</c>) to outgoing cloud-API requests. The server verifies the fingerprint
/// matches the one the seat was bound to, so a copied install/token can't be used elsewhere.
/// </summary>
public sealed class SeatAuthHandler : DelegatingHandler
{
    public const string HardwareHeader = "X-Hardware-Id";

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

        var hardwareId = _tokens.HardwareId;
        if (!string.IsNullOrWhiteSpace(hardwareId) && !request.Headers.Contains(HardwareHeader))
            request.Headers.TryAddWithoutValidation(HardwareHeader, hardwareId);

        return base.SendAsync(request, cancellationToken);
    }
}
