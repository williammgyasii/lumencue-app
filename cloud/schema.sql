-- LumenCue cloud schema (Neon Postgres).
-- Tenancy + org-level song sync. Applied by the auth/sync API on startup and idempotent here.

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

-- One row per signed-in device. Used to enforce the organization's seat_count.
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

-- Org-level song library. Sections are stored as JSON to mirror the desktop model.
-- updated_at + deleted drive last-write-wins sync with soft-delete tombstones.
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
