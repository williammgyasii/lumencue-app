using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Tenancy;
using ChurchProjection.Core.Services;
using Dapper;
using Npgsql;
using Serilog;

namespace ChurchProjection.Infrastructure.Services;

/// <summary>
/// Talks directly to the Neon Postgres backend (auth, seats, song sync) without a hosted API in
/// between. Selected when a Neon connection string is configured. This lets distributed builds
/// sign in and sync from anywhere, since Neon is reachable over the public internet.
/// </summary>
public sealed class NeonCloudGateway : ICloudGateway
{
    private readonly NpgsqlDataSource _dataSource;

    public NeonCloudGateway(string connectionString)
    {
        _dataSource = new NpgsqlDataSourceBuilder(Normalize(connectionString)).Build();
    }

    public bool IsConfigured => true;

    public async Task<SignInResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationCode) ||
            string.IsNullOrWhiteSpace(request.BranchCode) ||
            string.IsNullOrWhiteSpace(request.DeviceId))
            return SignInResult.Fail("Organization, branch and device are required.");

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

            var branch = await conn.QuerySingleOrDefaultAsync<BranchRow>(
                """
                select b.id, b.organization_id, b.name, b.password_hash,
                       o.name as organization_name, o.seat_count
                from branches b
                join organizations o on o.id = b.organization_id
                where b.organization_id = @Org and b.id = @Branch
                """,
                new { Org = request.OrganizationCode.Trim(), Branch = request.BranchCode.Trim() });

            if (branch is null || !VerifyPassword(request.Password, branch.password_hash))
                return SignInResult.Fail("Invalid organization, branch or password.");

            var existingToken = await conn.ExecuteScalarAsync<string?>(
                "select token from seats where organization_id = @Org and device_id = @Device",
                new { Org = branch.organization_id, Device = request.DeviceId });

            var token = existingToken;
            if (token is null)
            {
                var used = await conn.ExecuteScalarAsync<int>(
                    "select count(*) from seats where organization_id = @Org",
                    new { Org = branch.organization_id });

                if (used >= branch.seat_count)
                    return SignInResult.Fail($"All {branch.seat_count} seats for {branch.organization_name} are in use.");

                token = NewToken();
                await conn.ExecuteAsync(
                    """
                    insert into seats (organization_id, device_id, branch_id, token, claimed_at, last_seen_at)
                    values (@Org, @Device, @Branch, @Token, now(), now())
                    """,
                    new { Org = branch.organization_id, Device = request.DeviceId, Branch = branch.id, Token = token });
            }
            else
            {
                await conn.ExecuteAsync(
                    "update seats set branch_id = @Branch, last_seen_at = now() where organization_id = @Org and device_id = @Device",
                    new { Branch = branch.id, Org = branch.organization_id, Device = request.DeviceId });
            }

            return SignInResult.Ok(await BuildSessionAsync(conn, branch, request.DeviceId, token!));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Neon sign-in failed");
            return SignInResult.Fail("Could not reach the sign-in service.");
        }
    }

    public async Task<SignInResult> ValidateAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

            var seat = await conn.QuerySingleOrDefaultAsync<SeatRow>(
                "select organization_id, device_id, branch_id from seats where token = @Token",
                new { Token = session.Token });
            if (seat is null) return SignInResult.Fail("Session no longer valid.");

            await conn.ExecuteAsync("update seats set last_seen_at = now() where token = @Token", new { Token = session.Token });

            var branch = await conn.QuerySingleOrDefaultAsync<BranchRow>(
                """
                select b.id, b.organization_id, b.name, b.password_hash,
                       o.name as organization_name, o.seat_count
                from branches b join organizations o on o.id = b.organization_id
                where b.id = @Branch and b.organization_id = @Org
                """,
                new { Branch = seat.branch_id, Org = seat.organization_id });
            if (branch is null) return SignInResult.Fail("Branch no longer exists.");

            return SignInResult.Ok(await BuildSessionAsync(conn, branch, seat.device_id, session.Token));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Neon validate failed");
            return SignInResult.Fail("offline");
        }
    }

    public async Task SignOutAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await conn.ExecuteAsync("delete from seats where token = @Token", new { Token = session.Token });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Neon sign-out failed");
        }
    }

    public async Task<SongSyncBatch> PullSongsAsync(string organizationId, string? sinceCursor, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        var sinceUtc = DateTime.MinValue.ToUniversalTime();
        if (!string.IsNullOrWhiteSpace(sinceCursor) &&
            DateTime.TryParse(sinceCursor, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            sinceUtc = parsed;

        var rows = (await conn.QueryAsync<SongRow>(
            """
            select id, organization_id, title, artist, ccli_number, copyright_info,
                   tags, lines_per_slide, sections::text as sections, deleted, updated_at
            from songs
            where organization_id = @Org and updated_at > @Since
            order by updated_at
            """,
            new { Org = organizationId, Since = sinceUtc })).ToList();

        var songs = rows.Select(r => r.ToSong()).ToList();
        string? cursor = rows.Count > 0
            ? rows.Max(r => r.updated_at).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
            : sinceCursor;

        return new SongSyncBatch(songs, cursor);
    }

    public async Task PushSongsAsync(string organizationId, IReadOnlyList<Song> songs, CancellationToken cancellationToken = default)
    {
        if (songs.Count == 0) return;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        foreach (var song in songs)
        {
            var id = Guid.TryParse(song.CloudId, out var g) ? g : Guid.NewGuid();
            var sectionsJson = JsonSerializer.Serialize(song.Sections);

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
                    Org = organizationId,
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

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task<AuthSession> BuildSessionAsync(NpgsqlConnection conn, BranchRow branch, string deviceId, string token)
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

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Verifies a password against the API's "iterations.saltBase64.hashBase64" PBKDF2 format.</summary>
    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Accepts either an Npgsql key=value string or a <c>postgresql://</c> URI (Neon's default form).</summary>
    private static string Normalize(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);

        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            Database = uri.AbsolutePath.Trim('/'),
            SslMode = SslMode.Require,
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
            var val = Uri.UnescapeDataString(kv[1]);

            switch (key)
            {
                case "sslmode":
                    if (Enum.TryParse<SslMode>(val.Replace("-", ""), ignoreCase: true, out var ssl)) b.SslMode = ssl;
                    break;
                case "channel_binding":
                    if (Enum.TryParse<ChannelBinding>(val, ignoreCase: true, out var cb)) b.ChannelBinding = cb;
                    break;
            }
        }

        return b.ConnectionString;
    }

    // --- Dapper row DTOs (snake_case to match the cloud schema) ---

    private sealed class BranchRow
    {
        public string id { get; set; } = "";
        public string organization_id { get; set; } = "";
        public string name { get; set; } = "";
        public string password_hash { get; set; } = "";
        public string organization_name { get; set; } = "";
        public int seat_count { get; set; }
    }

    private sealed class SeatRow
    {
        public string organization_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string branch_id { get; set; } = "";
    }

    private sealed class SongRow
    {
        public Guid id { get; set; }
        public string organization_id { get; set; } = "";
        public string title { get; set; } = "";
        public string? artist { get; set; }
        public string? ccli_number { get; set; }
        public string? copyright_info { get; set; }
        public string? tags { get; set; }
        public int lines_per_slide { get; set; }
        public string? sections { get; set; }
        public bool deleted { get; set; }
        public DateTime updated_at { get; set; }

        public Song ToSong() => new()
        {
            CloudId = id.ToString(),
            OrganizationId = organization_id,
            Title = title,
            Artist = artist,
            CcliNumber = ccli_number,
            CopyrightInfo = copyright_info,
            Tags = tags,
            LinesPerSlide = lines_per_slide,
            Deleted = deleted,
            UpdatedAt = DateTime.SpecifyKind(updated_at, DateTimeKind.Utc),
            Sections = string.IsNullOrWhiteSpace(sections)
                ? []
                : JsonSerializer.Deserialize<List<SongSection>>(sections) ?? [],
        };
    }
}
