using Dapper;
using Npgsql;

namespace ChurchProjection.Api;

/// <summary>
/// Owns the cloud schema and seed data.
///
/// The tenancy model is <b>Organization → Branch → Seat → Device</b>:
/// <list type="bullet">
///   <item>An <b>organization</b> is a church (e.g. CEYC).</item>
///   <item>A <b>branch</b> is an operating unit with its own login, seats, plan and library
///   (e.g. Main, Teens, Youth). Billing and entitlements are per branch.</item>
///   <item>A <b>seat</b> is one active install, bound to a physical <b>device</b> (hardware id).</item>
/// </list>
///
/// Schema changes are applied via a single integer version in <c>schema_meta</c>. Bumping
/// <see cref="SchemaVersion"/> drops and recreates everything on next start — a deliberate,
/// one-time reset used while the product is pre-launch (no real customer data to preserve yet).
/// </summary>
public static class Db
{
    // Bump to force a destructive rebuild + reseed on next startup.
    //
    // WARNING: real tenants now exist (live churches). Do NOT bump this to ship schema changes —
    // a bump DROPS ALL DATA. Evolve the schema additively via idempotent statements in
    // <see cref="Migrations"/> instead. The destructive path is retained only for local/dev resets.
    private const int SchemaVersion = 2;

    private const string Schema = """
        create table if not exists schema_meta (version int not null);

        create table if not exists organizations (
            id          text primary key,
            name        text not null,
            created_at  timestamptz not null default now()
        );

        -- A branch owns its own login, seats, plan and (later) library. Code is unique per org.
        create table if not exists branches (
            organization_id text not null references organizations(id) on delete cascade,
            id              text not null,
            name            text not null,
            password_hash   text not null,
            created_at      timestamptz not null default now(),
            primary key (organization_id, id)
        );

        -- Plan catalogue. Prices are USD/seat/month; stt allowance is a per-month backstop.
        create table if not exists plans (
            code                  text primary key,
            name                  text not null,
            seats_default         int  not null,
            stt_minutes_per_month int  not null,
            price_usd_month       numeric not null,
            features              jsonb not null default '{}'::jsonb
        );

        -- Commercial state per branch. The provider_* columns are the seam for a future payment
        -- provider (Stripe/Paystack/etc.); null until one is wired in.
        create table if not exists subscriptions (
            organization_id         text not null,
            branch_id               text not null,
            plan_code               text not null references plans(code),
            quantity                int  not null default 1,
            status                  text not null default 'active',
            current_period_end      timestamptz,
            provider                text,
            provider_customer_id    text,
            provider_subscription_id text,
            created_at              timestamptz not null default now(),
            updated_at              timestamptz not null default now(),
            primary key (organization_id, branch_id),
            foreign key (organization_id, branch_id) references branches(organization_id, id) on delete cascade
        );

        -- Resolved entitlements the app reads each sign-in/validate (derived from plan + subscription).
        create table if not exists entitlements (
            organization_id       text not null,
            branch_id             text not null,
            seats                 int  not null,
            stt_minutes_per_month int  not null,
            features              jsonb not null default '{}'::jsonb,
            updated_at            timestamptz not null default now(),
            primary key (organization_id, branch_id),
            foreign key (organization_id, branch_id) references branches(organization_id, id) on delete cascade
        );

        -- Append-only audit of billing/seat lifecycle events (provider webhooks land here later).
        create table if not exists billing_events (
            id              bigserial primary key,
            organization_id text not null,
            branch_id       text not null,
            type            text not null,
            data            jsonb not null default '{}'::jsonb,
            created_at      timestamptz not null default now()
        );

        -- A claimed seat, bound to a physical machine (hardware_id). One seat per machine per branch.
        create table if not exists seats (
            id              bigserial primary key,
            organization_id text not null,
            branch_id       text not null,
            device_id       text not null,
            hardware_id     text not null,
            token           text not null,
            claimed_at      timestamptz not null default now(),
            last_seen_at    timestamptz not null default now(),
            foreign key (organization_id, branch_id) references branches(organization_id, id) on delete cascade,
            unique (organization_id, branch_id, hardware_id)
        );
        create index if not exists idx_seats_token on seats(token);

        -- Distinct machines that have ever activated a branch (rolling window powers the move limit).
        create table if not exists device_activations (
            organization_id text not null,
            branch_id       text not null,
            hardware_id     text not null,
            first_seen_at   timestamptz not null default now(),
            last_seen_at    timestamptz not null default now(),
            primary key (organization_id, branch_id, hardware_id),
            foreign key (organization_id, branch_id) references branches(organization_id, id) on delete cascade
        );

        -- Per-branch daily speech-to-text metering (cost backstop against runaway usage).
        create table if not exists stt_usage (
            organization_id text not null,
            branch_id       text not null,
            day             date not null,
            seconds_used    int  not null default 0,
            primary key (organization_id, branch_id, day),
            foreign key (organization_id, branch_id) references branches(organization_id, id) on delete cascade
        );

        -- Songs remain org-scoped for now (per-branch + shared library is the next phase).
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
        """;

    // Idempotent, additive migrations run on every startup once the base schema exists.
    // Now that real tenants exist, schema changes go here (not via a destructive version bump).
    private const string Migrations = """
        -- 2026-06: STT allowance moved from per-day to per-month. Rename the columns in place and
        -- backfill values once (the column guard makes each block run exactly once, at transition).
        do $$
        begin
            if exists (select 1 from information_schema.columns
                       where table_name = 'plans' and column_name = 'stt_minutes_per_day') then
                alter table plans rename column stt_minutes_per_day to stt_minutes_per_month;
                update plans set stt_minutes_per_month = case code
                    when 'trial'    then 600
                    when 'standard' then 2400
                    when 'pro'      then 6000
                    when 'master'   then 100000
                    else stt_minutes_per_month
                end;
            end if;

            if exists (select 1 from information_schema.columns
                       where table_name = 'entitlements' and column_name = 'stt_minutes_per_day') then
                alter table entitlements rename column stt_minutes_per_day to stt_minutes_per_month;
                update entitlements e set stt_minutes_per_month = p.stt_minutes_per_month
                from subscriptions s
                join plans p on p.code = s.plan_code
                where s.organization_id = e.organization_id and s.branch_id = e.branch_id;
            end if;
        end $$;

        -- 2026-06: premium feature flags per plan tier. The catalogue is product-defined, so this is
        -- kept authoritative on every deploy. Absent/false flag = the feature is locked on the client.
        update plans set features = case code
            when 'trial'    then '{"video_backgrounds":true,"shared_library":true,"multi_campus":false}'::jsonb
            when 'standard' then '{"video_backgrounds":false,"shared_library":false,"multi_campus":false}'::jsonb
            when 'pro'      then '{"video_backgrounds":true,"shared_library":true,"multi_campus":true}'::jsonb
            when 'master'   then '{"video_backgrounds":true,"shared_library":true,"multi_campus":true}'::jsonb
            else features
        end;
        """;

    private const string DropLegacy = """
        drop table if exists stt_usage cascade;
        drop table if exists device_activations cascade;
        drop table if exists billing_events cascade;
        drop table if exists entitlements cascade;
        drop table if exists subscriptions cascade;
        drop table if exists seats cascade;
        drop table if exists songs cascade;
        drop table if exists plans cascade;
        drop table if exists branches cascade;
        drop table if exists organizations cascade;
        """;

    public static async Task InitializeAsync(NpgsqlDataSource dataSource, ILogger logger)
    {
        await using var conn = await dataSource.OpenConnectionAsync();

        await conn.ExecuteAsync("create table if not exists schema_meta (version int not null);");
        var current = await conn.ExecuteScalarAsync<int?>("select version from schema_meta limit 1") ?? 0;

        if (current < SchemaVersion)
        {
            logger.LogWarning(
                "Schema version {Current} < {Target}: performing one-time destructive rebuild + reseed.",
                current, SchemaVersion);

            await conn.ExecuteAsync(DropLegacy);
            await conn.ExecuteAsync(Schema);
            await SeedAsync(conn, logger);
            await conn.ExecuteAsync("delete from schema_meta; insert into schema_meta (version) values (@V);",
                new { V = SchemaVersion });

            logger.LogInformation("Schema rebuilt to version {Version}.", SchemaVersion);
            return;
        }

        // Already current: ensure tables exist (no-op on a healthy DB) without touching data,
        // then apply additive migrations (idempotent) so schema evolves without data loss.
        await conn.ExecuteAsync(Schema);
        await conn.ExecuteAsync(Migrations);
        logger.LogInformation("Schema ready (version {Version}); migrations applied.", SchemaVersion);
    }

    private static async Task SeedAsync(NpgsqlConnection conn, ILogger logger)
    {
        // --- Plans ---
        await conn.ExecuteAsync(
            "insert into plans (code, name, seats_default, stt_minutes_per_month, price_usd_month, features) values (@Code, @Name, @Seats, @Stt, @Price, @Features::jsonb)",
            new[]
            {
                new { Code = "trial",    Name = "Trial",    Seats = 2,   Stt = 600,    Price = 0m,   Features = "{\"video_backgrounds\":true,\"shared_library\":true,\"multi_campus\":false}" },
                new { Code = "standard", Name = "Standard", Seats = 1,   Stt = 2400,   Price = 50m,  Features = "{\"video_backgrounds\":false,\"shared_library\":false,\"multi_campus\":false}" },
                new { Code = "pro",      Name = "Pro",      Seats = 1,   Stt = 6000,   Price = 100m, Features = "{\"video_backgrounds\":true,\"shared_library\":true,\"multi_campus\":true}" },
                new { Code = "master",   Name = "Master",   Seats = 999, Stt = 100000, Price = 0m,   Features = "{\"video_backgrounds\":true,\"shared_library\":true,\"multi_campus\":true}" },
            });

        // --- Demo / launch tenants. Password for every seeded branch is "lumen123". ---
        await SeedOrganizationAsync(conn, "ceyc", "CEYC",
            ("main", "Main Church", 1),
            ("teens", "Teens Church", 3),
            ("youth", "Youth Church", 2));

        await SeedOrganizationAsync(conn, "grace", "Grace Chapel",
            ("main", "Main Campus", 1));

        logger.LogInformation(
            "Seeded tenants. Try organization='ceyc', branch='teens', password='lumen123' (3 seats).");
    }

    private static async Task SeedOrganizationAsync(
        NpgsqlConnection conn, string orgId, string orgName,
        params (string Id, string Name, int Seats)[] branches)
    {
        await conn.ExecuteAsync(
            "insert into organizations (id, name) values (@Id, @Name)",
            new { Id = orgId, Name = orgName });

        foreach (var (id, name, seats) in branches)
        {
            await conn.ExecuteAsync(
                "insert into branches (organization_id, id, name, password_hash) values (@Org, @Id, @Name, @Hash)",
                new { Org = orgId, Id = id, Name = name, Hash = Passwords.Hash("lumen123") });

            await conn.ExecuteAsync(
                """
                insert into subscriptions (organization_id, branch_id, plan_code, quantity, status, current_period_end)
                values (@Org, @Branch, 'standard', @Qty, 'active', now() + interval '30 days')
                """,
                new { Org = orgId, Branch = id, Qty = seats });

            await conn.ExecuteAsync(
                """
                insert into entitlements (organization_id, branch_id, seats, stt_minutes_per_month)
                values (@Org, @Branch, @Seats, 2400)
                """,
                new { Org = orgId, Branch = id, Seats = seats });
        }
    }
}
