# LumenCue — Shared Library & Access/Anti-Abuse Design

Date: 2026-06-17
Status: Approved design (billing model still in design — see "Open: Billing")

## Context

LumenCue is live with real churches (e.g. CEYC). Two gaps surfaced:

1. **Access / cost leak.** The client device id is a random GUID stored locally, and the
   cloud API authorizes requests by *token existence only* — it never checks the request
   comes from the device that claimed the seat. A single paid seat's token can be copied to
   many machines, each streaming Deepgram STT, so cost is effectively uncapped. Seats are
   also pooled org-wide (`seat_count` lives on the organization), so there is no per-branch
   accounting.

2. **Library structure.** Songs are scoped to the *organization*, so every branch (Main,
   Teens, Youth) sees one merged list. There is no way to keep a branch's own library
   private while still intentionally sharing songs across the church.

## Mental model

```
Organization → Branch → Seat → Device
```

- **Organization** (e.g. CEYC): identity + the boundary of the Shared Library. No longer the
  seat pool.
- **Branch** (Main, Teens, Youth, Teens-1…): the independent operating unit — its own song
  library, its own seats, its own bill.
- **Seat**: one license to run LumenCue, owned by a branch.
- **Device**: the machine a seat is currently bound to (one at a time).

Build order: **Part A (Access & anti-abuse) first** — it is the security/cost fix and it
establishes "branch = independent, bound, metered unit", which Part B depends on. **Part B
(Per-branch libraries + Shared Library) second.**

---

## Part A — Access & anti-abuse

Layered, cheapest/highest-impact first:

1. **Hardware-derived device id.** Derive a stable fingerprint from the machine's identity
   (primarily the Windows machine GUID, plus a couple of stable signals), instead of a random
   GUID. Copying app data to another machine yields a different fingerprint. Allow one or two
   component changes (RAM/disk upgrade) without treating it as a new machine.

2. **Server-side seat↔device binding.** Store `hardware_id` on the seat. Re-check it on
   `/auth/validate`, `/stt/token`, `/bible/*`, and song-sync endpoints. A copied token used
   from a different machine is rejected. **This single change closes the expensive leak.**

3. **Per-branch seats.** Move `seat_count` off the organization and onto the branch. Count
   seats `where branch_id = …`. Teens can buy 3, Main 1, independently.

4. **Per-branch entitlements record.** One place per branch for numeric limits and feature
   flags: `seats`, `stt_minutes_per_day`, feature toggles. Enforced server-side, never
   hardcoded into business logic. This is also the extension point for grouped/consolidated
   billing later (no rewrite).

5. **STT metering.** `/stt/token` enforces a per-branch daily minute quota. Soft-warn near
   the cap, hard-stop over it. Guarantees the Deepgram bill cannot run away regardless of how
   machines are juggled.

6. **Device-move limit + concurrency guard.** A seat may move to a new machine but only ~3
   distinct machines / 30 days. One active machine per seat at a time. Stale seats (no
   check-in for ~14 days) auto-free.

### Machine breaks / switching machines

The seat is bound to *one machine at a time*, not welded forever. To switch, the operator
signs in on the new machine and the seat's stored fingerprint is overwritten (the dead
machine never needs to be touched). A self-service device list with a "Release" button lets a
branch free a seat on demand (support-side release acceptable for v1). The device-move limit
only bites users who move suspiciously often; STT metering caps cost even then.

---

## Part B — Per-branch libraries + Shared Library

- **Per-branch private libraries.** `Song` becomes branch-scoped (gains an owning
  `BranchId`). Song sync becomes branch-scoped (`…/branches/{branch}/songs`), reusing the
  existing cursor + soft-delete tombstone machinery.
- **One org-wide Shared Library** = a published catalog/noticeboard. **Any branch may
  publish.** Publishing does **not** push the song onto any other branch's service list.
- **Import = fork with provenance.** Importing copies the catalog snapshot into the branch's
  own library with a fresh cloud id and provenance fields (`SharedSourceId`,
  `SharedSourceVersion`). The copy is fully independent and editable; the publisher's later
  edits never change it. The copy can show an optional, opt-in "update available" nudge when
  the source version moves — never silent.
- **New `SharedSong` entity** (org-scoped): title, artist, CCLI, lyrics/sections snapshot,
  `SourceBranchId`, `Version`. Publishing bumps `Version`.
- **Deduplication.** Identity key, strongest first: **CCLI → provenance → normalized
  title+artist**. The noticeboard shows **one card per song**, sources grouped ("shared by
  Main, Youth"). Importing a song the branch likely already has **always asks**
  (Keep mine / Replace / Keep both).
- **Scope:** songs only for v1. Scripture (Bible API) and announcements/media are out of
  scope.
- **Migration (live users).** On rollout, **every branch keeps the full current list** as its
  private library — nobody loses anything before a service — and the Shared Library starts
  **empty**. Mechanically a one-time per-branch claim of existing songs so no device ends up
  with duplicates; exact steps to be detailed in the implementation plan.

---

## Decisions locked

- Adopt semantics: **fork-with-provenance** (not live subscription).
- Dedup on import: **always ask** on a likely match.
- Publishing permission: **any branch**.
- Migration: **copy full existing list into every branch**; Shared Library starts empty.
- Hierarchy: **Org → Branch** (2 real levels); branch is the seat/billing unit; per-branch
  **entitlements record** for extensibility.

## Gap → mitigation

| Gap / risk | Covered by |
|---|---|
| One seat's token copied to many machines | Hardware fingerprint + server-side token↔device binding |
| Deepgram cost runaway (abuse or honest) | Per-branch STT metering quota |
| Branches sharing one seat pool | Per-branch seats + entitlements |
| Machine breaks / needs swapping | Self-claim on new machine + stale auto-release |
| Rotating one login across many PCs | Device-move limit (~3 / 30 days) + concurrency guard |
| Merged, cluttered libraries | Per-branch private libraries |
| Sharing without forcing it on others | Shared Library (publish ≠ push) |
| Edits to a shared song breaking others | Fork-with-provenance |
| Duplicate songs everywhere | CCLI/provenance/fuzzy dedup, one card, always-ask import |
| Losing songs at rollout | Migration copies full list to every branch |
| Future consolidated billing for a ministry | Entitlements layer is extensible |

## Open: Billing

Pricing model, payment provider (note: Ghana-based — Paystack/Flutterwave vs Stripe),
currency, trial/free tier, and whether STT is included-with-cap vs usage-billed are still in
design and will be appended here before implementation.
