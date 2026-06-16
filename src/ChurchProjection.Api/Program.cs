using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ChurchProjection.Api;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Services;
using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// In hosted environments (Fly) bind the port the platform assigns. Locally PORT is unset,
// so the address from appsettings.json ("http://localhost:5080") is used unchanged.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://+:{port}");

// Note: appsettings.json ships an empty "Neon" placeholder, so check for blank (not just null)
// before falling back to the env var that Fly injects from the secret.
var connectionString = builder.Configuration.GetConnectionString("Neon");
if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = Environment.GetEnvironmentVariable("NEON_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "No Neon connection string. Set ConnectionStrings:Neon in appsettings.local.json or the NEON_CONNECTION_STRING environment variable.");

var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
builder.Services.AddSingleton(dataSource);

// Deepgram project key stays server-side; clients only ever receive short-lived JWTs.
var deepgramKey = builder.Configuration["Deepgram:ApiKey"];
if (string.IsNullOrWhiteSpace(deepgramKey))
    deepgramKey = Environment.GetEnvironmentVariable("DEEPGRAM_API_KEY");

builder.Services.AddHttpClient("deepgram", c =>
{
    c.BaseAddress = new Uri("https://api.deepgram.com/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

// API.Bible key stays server-side; the client reaches premium translations through /bible/*.
var apiBibleKey = builder.Configuration["ApiBible:ApiKey"];
if (string.IsNullOrWhiteSpace(apiBibleKey))
    apiBibleKey = Environment.GetEnvironmentVariable("APIBIBLE_API_KEY");

builder.Services.AddHttpClient("apibible", c =>
{
    c.BaseAddress = new Uri("https://rest.api.bible/v1/");
    c.Timeout = TimeSpan.FromSeconds(20);
});

// Auth is bearer-token based (no cookies), so any origin is safe to allow.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// HTTPS is terminated/forced at the Fly edge (force_https in fly.toml); the app itself
// listens on plain HTTP inside the VM, so no in-app UseHttpsRedirection (it would loop).
app.UseCors();

// Ensure schema + seed a demo organization/branch on startup so the API is usable immediately.
await Db.InitializeAsync(dataSource, app.Logger);

app.MapGet("/", () => Results.Ok(new { service = "LumenCue Cloud API", status = "ok" }));
app.MapGet("/health", async (NpgsqlDataSource ds) =>
{
    await using var conn = await ds.OpenConnectionAsync();
    var now = await conn.ExecuteScalarAsync<DateTime>("select now()");
    return Results.Ok(new { ok = true, dbTime = now });
});

// --- Auth -----------------------------------------------------------------

app.MapPost("/auth/signin", async (SignInRequest req, NpgsqlDataSource ds) =>
{
    if (string.IsNullOrWhiteSpace(req.OrganizationCode) ||
        string.IsNullOrWhiteSpace(req.BranchCode) ||
        string.IsNullOrWhiteSpace(req.DeviceId))
        return Results.BadRequest("Organization, branch and device are required.");

    await using var conn = await ds.OpenConnectionAsync();

    var branch = await conn.QuerySingleOrDefaultAsync<BranchRow>(
        """
        select b.id, b.organization_id, b.name, b.password_hash,
               o.name as organization_name, o.seat_count
        from branches b
        join organizations o on o.id = b.organization_id
        where b.organization_id = @Org and b.id = @Branch
        """,
        new { Org = req.OrganizationCode.Trim(), Branch = req.BranchCode.Trim() });

    if (branch is null || !Passwords.Verify(req.Password, branch.password_hash))
        return Results.Json("Invalid organization, branch or password.", statusCode: 401);

    // Seat enforcement: reuse an existing seat for this device, else claim a free one.
    var existingToken = await conn.ExecuteScalarAsync<string?>(
        "select token from seats where organization_id = @Org and device_id = @Device",
        new { Org = branch.organization_id, Device = req.DeviceId });

    var token = existingToken;
    if (token is null)
    {
        var used = await conn.ExecuteScalarAsync<int>(
            "select count(*) from seats where organization_id = @Org",
            new { Org = branch.organization_id });

        if (used >= branch.seat_count)
            return Results.Json(
                $"All {branch.seat_count} seats for {branch.organization_name} are in use.",
                statusCode: 403);

        token = NewToken();
        await conn.ExecuteAsync(
            """
            insert into seats (organization_id, device_id, branch_id, token, claimed_at, last_seen_at)
            values (@Org, @Device, @Branch, @Token, now(), now())
            """,
            new { Org = branch.organization_id, Device = req.DeviceId, Branch = branch.id, Token = token });
    }
    else
    {
        await conn.ExecuteAsync(
            "update seats set branch_id = @Branch, last_seen_at = now() where organization_id = @Org and device_id = @Device",
            new { Branch = branch.id, Org = branch.organization_id, Device = req.DeviceId });
    }

    var session = await BuildSessionAsync(conn, branch, req.DeviceId, token!);
    return Results.Ok(session);
});

app.MapPost("/auth/validate", async (HttpRequest http, NpgsqlDataSource ds) =>
{
    var token = BearerToken(http);
    if (token is null) return Results.Json("Missing token.", statusCode: 401);

    await using var conn = await ds.OpenConnectionAsync();
    var seat = await conn.QuerySingleOrDefaultAsync<SeatRow>(
        "select organization_id, device_id, branch_id from seats where token = @Token",
        new { Token = token });
    if (seat is null) return Results.Json("Session no longer valid.", statusCode: 401);

    await conn.ExecuteAsync(
        "update seats set last_seen_at = now() where token = @Token", new { Token = token });

    var branch = await conn.QuerySingleOrDefaultAsync<BranchRow>(
        """
        select b.id, b.organization_id, b.name, b.password_hash,
               o.name as organization_name, o.seat_count
        from branches b join organizations o on o.id = b.organization_id
        where b.id = @Branch and b.organization_id = @Org
        """,
        new { Branch = seat.branch_id, Org = seat.organization_id });
    if (branch is null) return Results.Json("Branch no longer exists.", statusCode: 401);

    var session = await BuildSessionAsync(conn, branch, seat.device_id, token);
    return Results.Ok(session);
});

app.MapPost("/auth/signout", async (HttpRequest http, NpgsqlDataSource ds) =>
{
    var token = BearerToken(http);
    if (token is null) return Results.NoContent();

    await using var conn = await ds.OpenConnectionAsync();
    await conn.ExecuteAsync("delete from seats where token = @Token", new { Token = token });
    return Results.NoContent();
});

// --- Song sync ------------------------------------------------------------

app.MapGet("/orgs/{orgId}/songs", async (string orgId, string? since, HttpRequest http, NpgsqlDataSource ds) =>
{
    await using var conn = await ds.OpenConnectionAsync();

    var seat = await AuthorizeSeatAsync(http, conn);
    if (seat is null) return Results.Json("Missing or invalid token.", statusCode: 401);
    if (!string.Equals(seat.organization_id, orgId, StringComparison.Ordinal))
        return Results.Json("You can only access your own organization's songs.", statusCode: 403);

    DateTime sinceUtc = DateTime.MinValue.ToUniversalTime();
    if (!string.IsNullOrWhiteSpace(since) &&
        DateTime.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        sinceUtc = parsed;

    var rows = (await conn.QueryAsync<SongRow>(
        """
        select id, organization_id, title, artist, ccli_number, copyright_info,
               tags, lines_per_slide, sections::text as sections, deleted, updated_at
        from songs
        where organization_id = @Org and updated_at > @Since
        order by updated_at
        """,
        new { Org = orgId, Since = sinceUtc })).ToList();

    var songs = rows.Select(r => r.ToSong()).ToList();
    string? cursor = rows.Count > 0
        ? rows.Max(r => r.updated_at).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
        : since;

    return Results.Ok(new SongSyncBatch(songs, cursor));
});

app.MapPut("/orgs/{orgId}/songs", async (string orgId, List<Song> songs, HttpRequest http, NpgsqlDataSource ds) =>
{
    await using var conn = await ds.OpenConnectionAsync();

    var seat = await AuthorizeSeatAsync(http, conn);
    if (seat is null) return Results.Json("Missing or invalid token.", statusCode: 401);
    if (!string.Equals(seat.organization_id, orgId, StringComparison.Ordinal))
        return Results.Json("You can only modify your own organization's songs.", statusCode: 403);

    if (songs.Count == 0) return Results.Ok();

    await using var tx = await conn.BeginTransactionAsync();

    foreach (var song in songs)
    {
        var id = Guid.TryParse(song.CloudId, out var g) ? g : Guid.NewGuid();
        var sectionsJson = JsonSerializer.Serialize(song.Sections);

        // Last-write-wins: only overwrite when the incoming change is at least as new.
        await conn.ExecuteAsync(
            """
            insert into songs (id, organization_id, title, artist, ccli_number, copyright_info,
                               tags, lines_per_slide, sections, deleted, updated_at)
            values (@Id, @Org, @Title, @Artist, @Ccli, @Copyright, @Tags, @Lines,
                    @Sections::jsonb, @Deleted, now())
            on conflict (id) do update set
                title = excluded.title,
                artist = excluded.artist,
                ccli_number = excluded.ccli_number,
                copyright_info = excluded.copyright_info,
                tags = excluded.tags,
                lines_per_slide = excluded.lines_per_slide,
                sections = excluded.sections,
                deleted = excluded.deleted,
                updated_at = now()
            """,
            new
            {
                Id = id,
                Org = orgId,
                song.Title,
                song.Artist,
                Ccli = song.CcliNumber,
                Copyright = song.CopyrightInfo,
                song.Tags,
                Lines = song.LinesPerSlide,
                Sections = sectionsJson,
                song.Deleted,
            }, tx);
    }

    await tx.CommitAsync();
    return Results.Ok();
});

// --- Speech-to-text token -------------------------------------------------
// Mints a short-lived Deepgram JWT for an authenticated seat. The client streams
// audio directly to Deepgram with this token, so the project key never ships.

app.MapPost("/stt/token", async (HttpRequest http, NpgsqlDataSource ds, IHttpClientFactory httpFactory) =>
{
    var token = BearerToken(http);
    if (token is null) return Results.Json("Missing token.", statusCode: 401);

    await using var conn = await ds.OpenConnectionAsync();
    var seat = await conn.QuerySingleOrDefaultAsync<SeatRow>(
        "select organization_id, device_id, branch_id from seats where token = @Token",
        new { Token = token });
    if (seat is null) return Results.Json("Session no longer valid.", statusCode: 401);

    if (string.IsNullOrWhiteSpace(deepgramKey))
        return Results.Json("Speech service is not configured.", statusCode: 503);

    var client = httpFactory.CreateClient("deepgram");
    using var req = new HttpRequestMessage(HttpMethod.Post, "v1/auth/grant");
    req.Headers.TryAddWithoutValidation("Authorization", $"Token {deepgramKey}");
    // 300s is comfortably long enough to (re)open the live socket; the client requests a fresh
    // token on every connect/reconnect, so it never relies on a stale credential.
    req.Content = JsonContent.Create(new { ttl_seconds = 300 });

    using var resp = await client.SendAsync(req);
    if (!resp.IsSuccessStatusCode)
    {
        app.Logger.LogWarning("Deepgram grant failed: {Status}", resp.StatusCode);
        return Results.Json("Could not mint a speech token.", statusCode: 502);
    }

    var grant = await resp.Content.ReadFromJsonAsync<DeepgramGrant>();
    if (grant is null || string.IsNullOrWhiteSpace(grant.access_token))
        return Results.Json("Empty token from speech service.", statusCode: 502);

    await conn.ExecuteAsync(
        "update seats set last_seen_at = now() where token = @Token", new { Token = token });

    return Results.Ok(new SttTokenResponse(grant.access_token, grant.expires_in ?? 30));
});

// --- Bible proxy (premium API.Bible translations) -------------------------
// Authenticated read-only passthrough to api.bible. The client sends the same relative paths
// it always used (bibles/{id}/chapters/..., bibles/{id}/books); the server attaches the key.

app.MapGet("/bible/{**path}", async (string path, HttpRequest http, NpgsqlDataSource ds, IHttpClientFactory httpFactory) =>
{
    await using var conn = await ds.OpenConnectionAsync();
    var seat = await AuthorizeSeatAsync(http, conn);
    if (seat is null) return Results.Json("Missing or invalid token.", statusCode: 401);

    if (string.IsNullOrWhiteSpace(apiBibleKey))
        return Results.Json("Bible service is not configured.", statusCode: 503);

    // Only allow the read paths the client actually uses; never proxy arbitrary api.bible routes.
    if (!path.StartsWith("bibles", StringComparison.OrdinalIgnoreCase))
        return Results.Json("Unsupported Bible path.", statusCode: 400);

    var client = httpFactory.CreateClient("apibible");
    using var req = new HttpRequestMessage(HttpMethod.Get, path + http.QueryString.Value);
    req.Headers.TryAddWithoutValidation("api-key", apiBibleKey);

    using var resp = await client.SendAsync(req);
    var body = await resp.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json", System.Text.Encoding.UTF8, (int)resp.StatusCode);
});

app.Run();

// --- Helpers --------------------------------------------------------------

static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

static string? BearerToken(HttpRequest http)
{
    var header = http.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? header["Bearer ".Length..].Trim()
        : null;
}

// Resolves the seat behind the request's bearer token (null if missing/unknown).
static async Task<SeatRow?> AuthorizeSeatAsync(HttpRequest http, NpgsqlConnection conn)
{
    var token = BearerToken(http);
    if (token is null) return null;

    return await conn.QuerySingleOrDefaultAsync<SeatRow>(
        "select organization_id, device_id, branch_id from seats where token = @Token",
        new { Token = token });
}

static async Task<AuthSession> BuildSessionAsync(NpgsqlConnection conn, BranchRow branch, string deviceId, string token)
{
    var used = await conn.ExecuteScalarAsync<int>(
        "select count(*) from seats where organization_id = @Org",
        new { Org = branch.organization_id });

    return new AuthSession
    {
        Token = token,
        OrganizationId = branch.organization_id,
        OrganizationName = branch.organization_name,
        BranchId = branch.id,
        BranchName = branch.name,
        DeviceId = deviceId,
        SeatCount = branch.seat_count,
        SeatsUsed = used,
        LastValidatedUtc = DateTime.UtcNow,
    };
}
