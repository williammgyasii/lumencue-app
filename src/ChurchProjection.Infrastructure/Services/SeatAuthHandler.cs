using System.Net;
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

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _tokens.CurrentToken;
        var hadToken = !string.IsNullOrWhiteSpace(token);
        if (hadToken)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var hardwareId = _tokens.HardwareId;
        if (!string.IsNullOrWhiteSpace(hardwareId) && !request.Headers.Contains(HardwareHeader))
            request.Headers.TryAddWithoutValidation(HardwareHeader, hardwareId);

        var response = await base.SendAsync(request, cancellationToken);

        // A 401 on a request that DID carry a token means the seat token is no longer accepted
        // (revoked, expired, or seat released elsewhere). Signal so the app can return to sign-in
        // rather than keep running on a session the server rejects.
        if (hadToken && response.StatusCode == HttpStatusCode.Unauthorized)
            _tokens.NotifyUnauthorized();

        return response;
    }
}
