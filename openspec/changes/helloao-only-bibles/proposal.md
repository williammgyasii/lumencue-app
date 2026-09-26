## Why

The API.Bible Pro plan is cancelled. The booth only needs a one-time download into SQLite. Paid NIV-family codes will fail on new desks; helloao already serves that download for public and open translations.

## What Changes

Explored: **A** drop API.Bible and offer helloao plus existing hosted JSON (chosen), **B** API.Bible Starter (rejected — non-commercial, still a monthly API), **C** host NIV/MSG files ourselves (rejected — those texts stay copyrighted).

- Picker curated list becomes helloao codes we can actually cache: `KJV`, `BSB`, `WEB`, `NET`, `ASV`, `LSV`, `YLT`, plus the hosted files already on `lumencue-releases` (`ESV`, `GNT`, `TPT`, `TLB`, `AMPC`)
- **BREAKING:** picker no longer offers `NIV`, `NKJV`, `NLT`, `MSG`, `AMP`, `CSB`. Desks that already cached those rows keep them locally; they are not re-downloaded
- `KJV` and `NET` MUST resolve to helloao ids (`eng_kjv`, `eng_net`) even though the catalog short names are `KJAV` and `NETB`
- Desktop stops calling the cloud `/bible/` proxy and does not construct `ApiBibleClient`
- Cloud `/bible/` proxy and `APIBIBLE_API_KEY` stay unused this change (old builds fail closed). No schema bump

**Not in this change:** spoken-name aliases (NIV still parses; the picker just will not have it), Compare slot rules, prefetch queue order, hosting new copyrighted JSON.

## Capabilities

### New Capabilities

- `helloao-only-bibles`: Which translation codes the picker offers after API.Bible is gone, and that `KJV` / `NET` still bulk-cache from helloao.

### Modified Capabilities

- None. `bible-picker-prefetch` still queues whatever the picker offers (selected first). Compare still shows chosen codes minus live. Those specs stay true with new example codes.

## Impact

- `BibleCacheService.CuratedTranslations` and helloao id aliases
- `FreeBibleApiClient` fallback ids (`KJV` → `eng_kjv`, `NET` → `eng_net`)
- `ContentSearchViewModel` seed list (overwritten on load; first paint must not show NIV)
- `App.axaml.cs` / `CombinedBibleService` — no `ApiBibleClient`
- Tests for the offered list and aliases
- Worker `/bible/` and `ApiBibleLiveKeyTests` left as-is
