using Dapper;
using Npgsql;

namespace ChurchProjection.Api;

/// <summary>Creates the schema (idempotent) and seeds a demo organization/branch on first run.</summary>
public static class Db
{
    private const string Schema = """
        create table if not exists organizations (
            id          text primary key,
            name        text not null,
            seat_count  int  not null default 5,
            created_at  timestamptz not null default now()
        );
        -- Branch code (id) is unique within an organization, not globally.
        create table if not exists branches (
            organization_id text not null references organizations(id) on delete cascade,
            id              text not null,
            name            text not null,
            password_hash   text not null,
            created_at      timestamptz not null default now(),
            primary key (organization_id, id)
        );
        create table if not exists seats (
            organization_id text not null references organizations(id) on delete cascade,
            device_id       text not null,
            branch_id       text not null,
            token           text not null,
            claimed_at      timestamptz not null default now(),
            last_seen_at    timestamptz not null default now(),
            primary key (organization_id, device_id),
            foreign key (organization_id, branch_id) references branches(organization_id, id) on delete cascade
        );
        create table if not exists songs (
            id              uuid primary key default gen_random_uuid(),
            organization_id text not null references organizations(id) on delete cascade,
            title           text not null,
            artist          text,
            ccli_number     text,
            copyright_info  text,
            tags            text,
            lines_per_slide int  not null default 0,
            sections        jsonb not null default '[]'::jsonb,
            deleted         boolean not null default false,
            updated_at      timestamptz not null default now()
        );
        create index if not exists idx_songs_org_updated on songs(organization_id, updated_at);
        create index if not exists idx_seats_token on seats(token);
        """;

    public static async Task InitializeAsync(NpgsqlDataSource dataSource, ILogger logger)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(Schema);

        var orgs = await conn.ExecuteScalarAsync<int>("select count(*) from organizations");
        if (orgs == 0)
        {
            await conn.ExecuteAsync(
                "insert into organizations (id, name, seat_count) values (@Id, @Name, @Seats)",
                new { Id = "grace", Name = "Grace Chapel", Seats = 5 });
            await conn.ExecuteAsync(
                "insert into branches (id, organization_id, name, password_hash) values (@Id, @Org, @Name, @Hash)",
                new { Id = "main", Org = "grace", Name = "Main Campus", Hash = Passwords.Hash("lumen123") });

            logger.LogInformation(
                "Seeded demo tenant. Sign in with organization='grace', branch='main', password='lumen123'.");
        }

        logger.LogInformation("Neon schema ready.");
    }
}
