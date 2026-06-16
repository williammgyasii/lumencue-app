"""One-off provisioning until the admin portal exists.

Rebuilds branches/seats with a per-organization branch key (so two orgs can both have a
"main" branch), then seeds the demo org and CEYC Airport City. Passwords are hashed
identically to the API's Passwords.Hash (PBKDF2-SHA256, 100k iters, 16-byte salt, 32-byte key).

Emits cloud/provision_ceyc.sql and prints the plaintext logins once.
"""

import base64
import hashlib
import secrets

ORGS = [
    # (org_id, org_name, seat_count, [(branch_code, branch_name, password)])
    ("grace", "Grace Chapel", 5, [("main", "Main Campus", "lumen123")]),
    ("ceyc-airport", "CEYC Airport City", 5, [
        ("main", "Airport City (Main)", None),
        ("youth", "Youth Church", None),
        ("teens", "Teens Church", None),
    ]),
]


def hash_pw(pw: str) -> str:
    salt = secrets.token_bytes(16)
    key = hashlib.pbkdf2_hmac("sha256", pw.encode(), salt, 100_000, dklen=32)
    return f"100000.{base64.b64encode(salt).decode()}.{base64.b64encode(key).decode()}"


def gen_password() -> str:
    return "ceyc-" + secrets.token_hex(3)


def s(v: str) -> str:
    return "'" + v.replace("'", "''") + "'"


lines = [
    "drop table if exists seats;",
    "drop table if exists branches;",
    "create table branches ("
    " organization_id text not null references organizations(id) on delete cascade,"
    " id text not null, name text not null, password_hash text not null,"
    " created_at timestamptz not null default now(),"
    " primary key (organization_id, id));",
    "create table seats ("
    " organization_id text not null references organizations(id) on delete cascade,"
    " device_id text not null, branch_id text not null, token text not null,"
    " claimed_at timestamptz not null default now(),"
    " last_seen_at timestamptz not null default now(),"
    " primary key (organization_id, device_id),"
    " foreign key (organization_id, branch_id) references branches(organization_id, id) on delete cascade);",
    "create index if not exists idx_seats_token on seats(token);",
]

report = []
for org_id, org_name, seats, branches in ORGS:
    lines.append(
        f"insert into organizations (id, name, seat_count) values ({s(org_id)}, {s(org_name)}, {seats}) "
        "on conflict (id) do update set name = excluded.name, seat_count = excluded.seat_count;"
    )
    org_creds = []
    for code, name, pw in branches:
        pw = pw or gen_password()
        org_creds.append((code, name, pw))
        lines.append(
            f"insert into branches (organization_id, id, name, password_hash) "
            f"values ({s(org_id)}, {s(code)}, {s(name)}, {s(hash_pw(pw))});"
        )
    report.append((org_id, org_name, seats, org_creds))

with open("cloud/provision_ceyc.sql", "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")

for org_id, org_name, seats, org_creds in report:
    print(f"\nORG  {org_id}  ({org_name})  seats={seats}")
    for code, name, pw in org_creds:
        print(f"  BRANCH\t{code}\t{name}\t{pw}")
