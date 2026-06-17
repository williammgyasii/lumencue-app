# LumenCue — Billing Model & In-App Paywall Design

Date: 2026-06-17
Status: Proposed (supersedes the "Billing model" section of
`2026-06-17-shared-library-and-access-design.md`)

## Context

The cloud API has moved off Fly.io/Neon onto **AWS** (ECS Fargate + RDS PostgreSQL +
ALB + Route 53), so the cost basis and the billing model are being re-evaluated. The
tenancy/entitlements foundation from the access design is already shipped:
`Organization → Branch → Seat → Device`, hardware-bound seats, per-branch entitlements,
and server-side STT metering.

This spec decides **what we bill on**, **how the tiers differ**, and **how the app
enforces it with an in-app paywall**.

## Decisions locked (this session)

- **Billing unit: per active seat / month.** A seat = one active device bound to a
  branch. Flat and predictable (what churches budget for); scales naturally with church
  size and usage.
- **AI is the anchor value, included as a monthly allowance — never metered to the
  customer.** The allowance is a COGS ceiling, not a usage meter the church sees.
- **Tiers differ on AI allowance + premium features** (not price alone).
- **Plans:** `trial`, `standard`, `pro`, `master`.
- **STT metering moves from daily to a monthly allowance** (a daily cap can't bound
  monthly cost; see "Why monthly").
- **An in-app paywall blocks premium features** when the plan/subscription doesn't cover
  them, with graceful degradation so a church is never cut off mid-service.
- **AI monthly allowances:** trial **600**, standard **2,400**, pro **6,000** min;
  master unlimited.
- **On-ramp: trial-only** (7-day, full-feature). No free tier.
- **Lapsed subscription = graceful:** premium/AI stops, local projection keeps working —
  never a mid-service hard cut.

## Selling features → why per-seat + AI allowance

Ranked by purchase-driving differentiation:

1. **AI live transcription → hands-free slide advance.** The unique hook *and* the only
   feature with real per-use cost (Deepgram). Everything below is near-zero marginal cost.
2. **Cloud library + multi-device / multi-campus sync.**
3. **Premium Bible translations** (API.Bible).
4. **Professional visual output** — themes, video backgrounds, lower-thirds.
5. **Multi-branch / campus management + shared library.**

Only AI scales your cost, so AI is both the value anchor and the COGS driver. But churches
buy on flat annual budgets and reject metered bills — so we bill on **seats** (predictable,
size/usage-correlated) and **bundle AI as an allowance**, differentiating tiers by that
allowance plus premium feature gating.

## Plan catalogue

Per seat / month. AI allowances **confirmed**. Deepgram multilingual planning rate
**$0.0058/min**; "max STT cost" assumes every included minute is used (real usage is far
lower).

| plan | $/seat | AI allowance / mo | seats default | max STT cost | margin @ price |
|---|---|---|---|---|---|
| `trial` | $0 | 600 min (10 hr) | 2 | ~$3.50 | — (7-day full-feature evaluation) |
| `standard` | $50 | 2,400 min (40 hr) | 1 | ~$14 | ~72% |
| `pro` | $100 | 6,000 min (100 hr) | 1 | ~$35 | ~65% |
| `master` | $0 | unlimited | 999 | — | internal / owner |

### Feature matrix

| feature | trial | standard | pro | master |
|---|---|---|---|---|
| AI live transcription | ✓ (allowance) | ✓ | ✓ | ✓ unlimited |
| Cloud library + sync | ✓ | ✓ | ✓ | ✓ |
| Premium Bible translations | ✓ | ✓ | ✓ | ✓ |
| Themes (core) | ✓ | ✓ | ✓ | ✓ |
| Video backgrounds / lower-thirds | ✓ | — | ✓ | ✓ |
| Shared library across branches | ✓ | — | ✓ | ✓ |
| Multi-campus admin + priority support | — | — | ✓ | ✓ |

> Trial is deliberately full-feature so churches experience Pro-level capability before the
> wall comes down.

### Why monthly (not daily)

A daily cap must be ≥ the busiest single day (a 2-service Sunday ≈ 4 hr), but then the
30-day ceiling is ~30× that — at 360 min/day that's 10,800 min ≈ **$63**, more than the $50
charged. A monthly allowance bounds cost regardless of how the church spreads usage, allows
a heavy Sunday, and gives a real per-tier lever. The existing daily `stt_usage` rows are
kept; the check sums the **current calendar month** against the allowance.

## Cost basis on AWS (2026)

- **Deepgram Nova-3 streaming** (dominant, variable COGS): **$0.0048/min** monolingual,
  **$0.0058/min** multilingual (PAYG); cheaper on Growth once volume passes ~$4k/yr.
- **Fixed infra (shared across all churches):**
  - ALB ≈ **$16–18/mo**
  - Fargate 0.25 vCPU / 0.5 GB, 1 task ≈ **$9/mo**
  - RDS `db.t4g.micro` = **$0** for 12 months (free tier), then ~$12–15/mo
  - Route 53 hosted zone ~$0.50/mo; ECR/data negligible
  - → ~**$27/mo today**, ~**$45/mo** after the RDS free tier — amortized to ~$1–3/seat at
    modest scale.
- Real cost per active seat ≈ **$5–15/mo** depending on usage; margins ~72–90%.

## Enforcement architecture (server)

The app only ever **reads** a branch's resolved **entitlements**; a payment provider only
**writes** them. No provider call at runtime.

1. **Resolve + surface entitlements.** `LoadAccessAsync` already returns seats, STT
   allowance, plan, status, period end. Add the **`features` map** and include it (plus AI
   allowance + AI used-this-month) in the `/auth/signin` and `/auth/validate`
   responses (`AuthSession`), so the client can gate UI.
2. **Monthly STT metering.** `/stt/token` sums `stt_usage` over the current month and
   rejects once the monthly allowance is reached (HTTP 429, "resets next month"). Soft-warn
   near the cap.
3. **Server-enforced features = the costly/valuable ones:** AI (`/stt/token`), premium
   Bible (`/bible/*`), shared-library endpoints. These check `features` server-side so they
   can't be bypassed.
4. **Client-only features = purely local** (video backgrounds, lower-thirds, theme options):
   gated in the UI only. Acceptable for v1 (desktop app, low-stakes if bypassed); revisit if
   a feature becomes worth hard-enforcing.
5. **Subscription lifecycle:** `trial → active → past_due → suspended → canceled`. `trial`
   is active only until `current_period_end` (already shipped). Suspension **degrades
   gracefully** (premium stops at period end; local projection keeps working) — never cut a
   church off mid-service.

## In-app paywall (client)

### Entitlement state

Extend `AuthSession` and a client `EntitlementState` the UI binds to:
`PlanCode`, `SubscriptionStatus`, `TrialEndsUtc`, `AiMinutesAllowance`, `AiMinutesUsed`,
and a `Features` set (e.g. `video_backgrounds`, `shared_library`, `multi_campus`). A single
`IEntitlementService` exposes booleans like `CanUseVideoBackgrounds`,
`CanUseSharedLibrary`, `AiMinutesRemaining`, `IsInGracePeriod`.

### Three gate layers

1. **Subscription gate (app entry).**
   - *Trial active:* full app + persistent "N days left — Upgrade" banner.
   - *Trial expired / canceled:* **paywall screen** on launch — sign-in returns 403; the app
     shows "Your trial has ended / subscription inactive — Upgrade to continue" with an
     upgrade CTA. Basic **offline projection of the local library still works** (graceful);
     cloud + AI are blocked.
   - *past_due / suspended:* grace mode — a warning bar; premium (AI, sync, premium Bible)
     stops at period end, local projection continues. Never a mid-service hard cut.
2. **Feature gate (per premium feature).** Locked controls show a small lock + "Upgrade to
   Pro" affordance; clicking opens an upgrade prompt. The rest of the app is unaffected
   (e.g. a `standard` church projects normally but can't add a video background).
3. **Usage gate (AI allowance).** When the monthly AI allowance is exhausted, the
   AI-listening toggle is disabled with "Monthly AI limit reached — resets {date} or upgrade
   for more." Everything else keeps working; manual slide control is always available.

### Reusable UI

- `PaywallOverlay` / `UpgradePromptDialog` (shared component): title, the gated benefit, the
  plan that unlocks it, and an **Upgrade** action.
- `TrialBanner` / `GraceBanner` (dismissible-per-session status strips).
- A dedicated **Upgrade screen** listing tiers, current plan highlighted.

### Upgrade flow (v1)

`ManualBillingProvider` today: the Upgrade CTA opens a hosted checkout link or a
"contact us" flow; an admin/webhook flips `subscription.status` + `plan_code` and writes
`entitlements`; on next `/auth/validate` the client picks up the new entitlements and the
paywall lifts. Adding Paystack/Flutterwave/Stripe later = one adapter + webhook, **no
desktop-app changes**.

## Build order

1. **Server: monthly STT allowance** — sum month, update plans/entitlements to monthly
   figures, update the 429 message. (Small.)
2. **Server: surface `features` + AI allowance/used in `AuthSession`**; gate `/bible/*` and
   shared-library endpoints on features. (Small–medium.)
3. **Client: `EntitlementState` + `IEntitlementService`** reading the session. (Medium.)
4. **Client: the three gate layers + reusable paywall UI.** (Medium.)
5. **Upgrade flow (manual) + later a real provider adapter.** (Separate.)

## Open questions

- **Client-side feature gating trust** — accept bypassable client gates for v1, or invest in
  server enforcement for video/themes too?
- **Payment provider + regional/PPP pricing** vs flat USD — decide when wiring real charging
  (Ghana-based founder → Paystack/Flutterwave likely; Stripe for global).
- **Annual billing** (~2 months free) — include at launch?
