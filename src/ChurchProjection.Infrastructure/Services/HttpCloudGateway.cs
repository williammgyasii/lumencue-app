using System.Net.Http.Json;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Talks to the hosted auth/sync API over HTTPS. Selected automatically when <c>CloudApi:BaseUrl</c>
/// is configured. The API keeps the Neon credentials server-side; the client only carries a token.
/// The injected <see cref="HttpClient"/> must attach the seat token (via SeatAuthHandler) so the
/// authenticated song-sync endpoints work; sign-in itself runs before a token exists and needs none.
/// </summary>
public sealed class HttpCloudGateway : ICloudGateway
{
    private readonly HttpClient _http;

    public HttpCloudGateway(HttpClient http)
    {
        _http = http;
    }

    public bool IsConfigured => true;

    public async Task<SignInResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("auth/signin", request, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return SignInResult.Fail(await SafeErrorAsync(resp, cancellationToken));

            var session = await resp.Content.ReadFromJsonAsync<AuthSession>(cancellationToken);
            return session is null ? SignInResult.Fail("Empty response from server.") : SignInResult.Ok(session);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cloud sign-in failed");
            return SignInResult.Fail("Could not reach the sign-in service.");
        }
    }

    public async Task<SignInResult> ValidateAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "auth/validate");
            req.Headers.Authorization = new("Bearer", session.Token);
            var resp = await _http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return SignInResult.Fail(await SafeErrorAsync(resp, cancellationToken));

            var updated = await resp.Content.ReadFromJsonAsync<AuthSession>(cancellationToken);
            return SignInResult.Ok(updated ?? session);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cloud validate failed");
            return SignInResult.Fail("offline");
        }
    }

    public async Task SignOutAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "auth/signout");
            req.Headers.Authorization = new("Bearer", session.Token);
            await _http.SendAsync(req, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cloud sign-out failed");
        }
    }

    public async Task<SongSyncBatch> PullSongsAsync(string organizationId, string? sinceCursor, CancellationToken cancellationToken = default)
    {
        var url = $"orgs/{Uri.EscapeDataString(organizationId)}/songs";
        if (!string.IsNullOrWhiteSpace(sinceCursor))
            url += $"?since={Uri.EscapeDataString(sinceCursor)}";

        var batch = await _http.GetFromJsonAsync<SongSyncBatch>(url, cancellationToken);
        return batch ?? new SongSyncBatch(Array.Empty<Song>(), sinceCursor);
    }

    public async Task PushSongsAsync(string organizationId, IReadOnlyList<Song> songs, CancellationToken cancellationToken = default)
    {
        if (songs.Count == 0) return;
        await _http.PutAsJsonAsync($"orgs/{Uri.EscapeDataString(organizationId)}/songs", songs, cancellationToken);
    }

    private static async Task<string> SafeErrorAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        try
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(body) ? $"Server error ({(int)resp.StatusCode})." : body;
        }
        catch
        {
            return $"Server error ({(int)resp.StatusCode}).";
        }
    }
}
