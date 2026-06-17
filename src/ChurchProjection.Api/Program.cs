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

// Ensure schema + seed tenants on startup so the API is usable immediately.
await Db.InitializeAsync(dataSource, app.Logger);

// Anti-abuse tuning. A seat idle longer than the active window auto-frees so a dead machine never
// blocks a branch; the move limit caps distinct machines a branch can activate in a rolling window.
const int ActiveSeatWindowDays = 14;
const int MoveWindowDays = 30;
const int MoveSlack = 3; // distinct machines allowed beyond the seat count per move window

app.MapGet("/", () => Results.Ok(new { service = "LumenCue Cloud API", status = "ok" }));
app.MapGet("/health", async (NpgsqlDataSource ds) =>
{
    await using var conn = await ds.OpenConnectionAsync();
    var now = await conn.ExecuteScalarAsync<DateTime>("select now()");
    return Results.Ok(new { ok = true, dbTime = now });
});

// --- Auth -----------------------------------------------------------------

app.MapPost("/auth/signin", async (SignInRequest req, HttpRequest http, NpgsqlDataSource ds) =>
{
    if (string.IsNullOrWhiteSpace(req.OrganizationCode) ||
        string.IsNullOrWhiteSpace(req.BranchCode) ||
        string.IsNullOrWhiteSpace(req.DeviceId))
        return Results.BadRequest("Organization, branch and device are required.");

    // Hardware id binds the seat to a physical machine; it may arrive in the body or the header.
    var hardwareId = !string.IsNullOrWhiteSpace(req.HardwareId) ? req.HardwareId.Trim() : HardwareId(http);
    if (string.IsNullOrWhiteSpace(hardwareId))
        return Results.BadRequest("This device could not be identified. Please update the app.");

    await using var conn = await ds.OpenConnectionAsync();

    var branch = await LoadBranchAsync(conn, req.OrganizationCode.Trim(), req.BranchCode.Trim());
    if (branch is null || !Passwords.Verify(req.Password, branch.password_hash))
        return Results.Json("Invalid organization, branch or password.", statusCode: 401);

    var access = await LoadAccessAsync(conn, branch.organization_id, branch.id);
    if (!IsAccessActive(access))
        return Results.Json(InactiveReason(branch.name, access), statusCode: 403);

    // One seat per machine per branch: reuse this device's seat if it already holds one.
    var seat = await conn.QuerySingleOrDefaultAsync<SeatRow>(
        "select id, organization_id, branch_id, device_id, hardware_id from seats where organization_id = @Org and branch_id = @Branch and hardware_id = @Hw",
        new { Org = branch.organization_id, Branch = branch.id, Hw = hardwareId });

    var token = NewToken();
    if (seat is not null)
    {
        await conn.ExecuteAsync(
            "update seats set token = @Token, device_id = @Device, last_seen_at = now() where id = @Id",
            new { Token = token, Device = req.DeviceId, Id = seat.id });
    }
    else
    {
        // Active-seat check (idle seats past the window auto-free, so a dead machine never blocks a branch).
        var used = await ActiveSeatsAsync(conn, branch.organization_id, branch.id, ActiveSeatWindowDays);
        if (used >= access.seats)
            return Results.Json(
                $"All {access.seats} seat(s) for {branch.name} are in use. Release one or contact your administrator.",
                statusCode: 403);

        // Device-move limit: cap how many distinct machines a branch can activate in a rolling window,
        // so seats can't be rotated across many computers to dodge the seat count.
        var recentDevices = await conn.ExecuteScalarAsync<int>(
            """
            select count(*) from device_activations
            where organization_id = @Org and branch_id = @Branch
              and last_seen_at > now() - (@Days || ' days')::interval
            """,
            new { Org = branch.organization_id, Branch = branch.id, Days = MoveWindowDays });
        if (recentDevices >= access.seats + MoveSlack)
            return Results.Json(
                "Too many new devices have been activated for this branch recently. Please try again later or contact support.",
                statusCode: 403);

        await conn.ExecuteAsync(
            """
            insert into seats (organization_id, branch_id, device_id, hardware_id, token, claimed_at, last_seen_at)
            values (@Org, @Branch, @Device, @Hw, @Token, now(), now())
            """,
            new { Org = branch.organization_id, Branch = branch.id, Device = req.DeviceId, Hw = hardwareId, Token = token });
    }

    await TouchDeviceActivationAsync(conn, branch.organization_id, branch.id, hardwareId);

    var session = await BuildSessionAsync(conn, branch, req.DeviceId, token, access);
    return Results.Ok(session);
});

app.MapPost("/auth/validate", async (HttpRequest http, NpgsqlDataSource ds) =>
{
    var token = BearerToken(http);
    if (token is null) return Results.Json("Missing token.", statusCode: 401);

    var hardwareId = HardwareId(http);

    await using var conn = await ds.OpenConnectionAsync();
    var seat = await conn.QuerySingleOrDefaultAsync<SeatRow>(
        "select id, organization_id, branch_id, device_id, hardware_id from seats where token = @Token",
        new { Token = token });
    if (seat is null) return Results.Json("Session no longer valid.", statusCode: 401);

    // The seat is bound to the machine it was claimed on; a copied token from another device is rejected.
    if (string.IsNullOrWhiteSpace(hardwareId) || !string.Equals(hardwareId, seat.hardware_id, StringComparison.Ordinal))
        return Results.Json("This session is bound to a different device. Please sign in again.", statusCode: 401);

    var branch = await LoadBranchByIdAsync(conn, seat.organization_id, seat.branch_id);
    if (branch is null) return Results.Json("Branch no longer exists.", statusCode: 401);

    var access = await LoadAccessAsync(conn, seat.organization_id, seat.branch_id);
    if (!IsAccessActive(access))
        return Results.Json(InactiveReason(branch.name, access), statusCode: 403);

    await conn.ExecuteAsync("update seats set last_seen_at = now() where id = @Id", new { Id = seat.id });
    await TouchDeviceActivationAsync(conn, seat.organization_id, seat.branch_id, seat.hardware_id);

    var session = await BuildSessionAsync(conn, branch, seat.device_id, token, access);
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

// Grants are short-lived (GrantTtlSeconds) and the client refreshes a little before expiry, so each
// grant maps to roughly one window of live streaming. We meter by booking that window's worth of
// usage per grant: a conservative cost backstop (it slightly over-counts) that needs no client trust.
const int GrantTtlSeconds = 300;

app.MapPost("/stt/token", async (HttpRequest http, NpgsqlDataSource ds, IHttpClientFactory httpFactory) =>
{
    await using var conn = await ds.OpenConnectionAsync();

    var seat = await AuthorizeSeatAsync(http, conn);
    if (seat is null) return Results.Json("Missing or invalid token for this device.", statusCode: 401);

    var access = await LoadAccessAsync(conn, seat.organization_id, seat.branch_id);
    if (!IsAccessActive(access))
        return Results.Json("Your branch's subscription is inactive.", statusCode: 403);
    if (access.stt_minutes_per_month <= 0)
        return Results.Json("AI listening isn't included in your plan. Upgrade to enable it.", statusCode: 403);

    // Monthly quota: reject once this calendar month's metered usage has reached the branch's
    // allowance. Daily rows are still written; we sum them from the start of the UTC month.
    var usedSeconds = await MonthlySttSecondsAsync(conn, seat.organization_id, seat.branch_id);
    if (usedSeconds >= access.stt_minutes_per_month * 60)
        return Results.Json("Monthly AI-listening limit reached. It resets next month.", statusCode: 429);

    if (string.IsNullOrWhiteSpace(deepgramKey))
        return Results.Json("Speech service is not configured.", statusCode: 503);

    var client = httpFactory.CreateClient("deepgram");
    using var req = new HttpRequestMessage(HttpMethod.Post, "v1/auth/grant");
    req.Headers.TryAddWithoutValidation("Authorization", $"Token {deepgramKey}");
    req.Content = JsonContent.Create(new { ttl_seconds = GrantTtlSeconds });

    using var resp = await client.SendAsync(req);
    if (!resp.IsSuccessStatusCode)
    {
        app.Logger.LogWarning("Deepgram grant failed: {Status}", resp.StatusCode);
        return Results.Json("Could not mint a speech token.", statusCode: 502);
    }

    var grant = await resp.Content.ReadFromJsonAsync<DeepgramGrant>();
    if (grant is null || string.IsNullOrWhiteSpace(grant.access_token))
        return Results.Json("Empty token from speech service.", statusCode: 502);

    // Book this grant's window against today's usage and keep the seat fresh.
    await conn.ExecuteAsync(
        """
        insert into stt_usage (organization_id, branch_id, day, seconds_used)
        values (@Org, @Branch, (now() at time zone 'utc')::date, @Secs)
        on conflict (organization_id, branch_id, day)
        do update set seconds_used = stt_usage.seconds_used + excluded.seconds_used
        """,
        new { Org = seat.organization_id, Branch = seat.branch_id, Secs = GrantTtlSeconds });
    await conn.ExecuteAsync("update seats set last_seen_at = now() where id = @Id", new { Id = seat.id });

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

    // Premium translations are a paid benefit: cut off when the branch's subscription lapses
    // (graceful degradation — the client still has its bundled/offline translations).
    var access = await LoadAccessAsync(conn, seat.organization_id, seat.branch_id);
    if (!IsAccessActive(access))
        return Results.Json("Premium Bible translations require an active subscription.", statusCode: 403);

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

// The hardware fingerprint the client sends on every authenticated request.
static string HardwareId(HttpRequest http) => http.Headers["X-Hardware-Id"].ToString().Trim();

// A subscription that permits use. 'active' is paid/in-good-standing; 'trial' is allowed only until
// its current_period_end passes (after that the branch is locked until it converts to a paid plan).
// Anything else (suspended/canceled/past_due) blocks.
static bool IsAccessActive(AccessRow access) => access.status switch
{
    "active" => true,
    "trial"  => access.current_period_end is null || access.current_period_end.Value > DateTime.UtcNow,
    _        => false,
};

// Human-readable reason for a blocked branch, distinguishing an expired trial from other states.
static string InactiveReason(string branchName, AccessRow access) =>
    access.status == "trial"
        ? $"The free trial for {branchName} has ended. Upgrade to keep using LumenCue."
        : $"The subscription for {branchName} is {access.status}. Contact your administrator.";

// Resolves the seat behind the request's bearer token AND verifies it is being used from the device
// it was bound to. Returns null (→ 401) for a missing token or a hardware-id mismatch (copied token).
static async Task<SeatRow?> AuthorizeSeatAsync(HttpRequest http, NpgsqlConnection conn)
{
    var token = BearerToken(http);
    if (token is null) return null;

    var seat = await conn.QuerySingleOrDefaultAsync<SeatRow>(
        "select id, organization_id, branch_id, device_id, hardware_id from seats where token = @Token",
        new { Token = token });
    if (seat is null) return null;

    var hardwareId = HardwareId(http);
    if (string.IsNullOrWhiteSpace(hardwareId) || !string.Equals(hardwareId, seat.hardware_id, StringComparison.Ordinal))
        return null;

    return seat;
}

static Task<BranchRow?> LoadBranchAsync(NpgsqlConnection conn, string org, string branch) =>
    conn.QuerySingleOrDefaultAsync<BranchRow>(
        """
        select b.id, b.organization_id, b.name, b.password_hash, o.name as organization_name
        from branches b join organizations o on o.id = b.organization_id
        where b.organization_id = @Org and b.id = @Branch
        """,
        new { Org = org, Branch = branch });

static Task<BranchRow?> LoadBranchByIdAsync(NpgsqlConnection conn, string org, string branch) =>
    LoadBranchAsync(conn, org, branch);

// Joins resolved entitlements with the subscription status. Falls back to a safe "free, none" shape
// if rows are missing, so a misconfigured branch fails closed rather than granting unlimited access.
static async Task<AccessRow> LoadAccessAsync(NpgsqlConnection conn, string org, string branch)
{
    var row = await conn.QuerySingleOrDefaultAsync<AccessRow>(
        """
        select e.seats, e.stt_minutes_per_month, s.plan_code, s.status, s.current_period_end,
               coalesce(p.features::text, '{}') as features
        from entitlements e
        join subscriptions s on s.organization_id = e.organization_id and s.branch_id = e.branch_id
        join plans p on p.code = s.plan_code
        where e.organization_id = @Org and e.branch_id = @Branch
        """,
        new { Org = org, Branch = branch });

    return row ?? new AccessRow { seats = 1, stt_minutes_per_month = 0, plan_code = "free", status = "suspended", features = "{}" };
}

static Task<int> ActiveSeatsAsync(NpgsqlConnection conn, string org, string branch, int windowDays) =>
    conn.ExecuteScalarAsync<int>(
        """
        select count(*) from seats
        where organization_id = @Org and branch_id = @Branch
          and last_seen_at > now() - (@Days || ' days')::interval
        """,
        new { Org = org, Branch = branch, Days = windowDays });

static Task TouchDeviceActivationAsync(NpgsqlConnection conn, string org, string branch, string hardwareId) =>
    conn.ExecuteAsync(
        """
        insert into device_activations (organization_id, branch_id, hardware_id, first_seen_at, last_seen_at)
        values (@Org, @Branch, @Hw, now(), now())
        on conflict (organization_id, branch_id, hardware_id)
        do update set last_seen_at = now()
        """,
        new { Org = org, Branch = branch, Hw = hardwareId });

// Sum of AI-listening seconds metered for a branch since the start of the current UTC month.
static Task<int> MonthlySttSecondsAsync(NpgsqlConnection conn, string org, string branch) =>
    conn.ExecuteScalarAsync<int>(
        """
        select coalesce(sum(seconds_used), 0) from stt_usage
        where organization_id = @Org and branch_id = @Branch
          and day >= date_trunc('month', (now() at time zone 'utc'))::date
        """,
        new { Org = org, Branch = branch });

// Enabled feature keys from the resolved feature JSON ({"video_backgrounds":true,...}).
static List<string> ParseFeatures(string json)
{
    if (string.IsNullOrWhiteSpace(json)) return [];
    try
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
        return map is null ? [] : map.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
    }
    catch
    {
        return [];
    }
}

static async Task<AuthSession> BuildSessionAsync(
    NpgsqlConnection conn, BranchRow branch, string deviceId, string token, AccessRow access)
{
    var used = await ActiveSeatsAsync(conn, branch.organization_id, branch.id, 14);
    var sttSeconds = await MonthlySttSecondsAsync(conn, branch.organization_id, branch.id);

    return new AuthSession
    {
        Token = token,
        OrganizationId = branch.organization_id,
        OrganizationName = branch.organization_name,
        BranchId = branch.id,
        BranchName = branch.name,
        DeviceId = deviceId,
        SeatCount = access.seats,
        SeatsUsed = used,
        PlanCode = access.plan_code,
        SubscriptionStatus = access.status,
        CurrentPeriodEndUtc = access.current_period_end,
        SttMinutesPerMonth = access.stt_minutes_per_month,
        SttMinutesUsed = sttSeconds / 60,
        Features = ParseFeatures(access.features),
        LastValidatedUtc = DateTime.UtcNow,
    };
}
