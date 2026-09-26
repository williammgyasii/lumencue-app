## Context

See proposal.md. `BibleCacheService.CuratedTranslations` still lists NIV-family codes that only `ApiBibleClient` could download. Helloao catalog short names are `KJAV` / `NETB`, so a raw catalog import drops our booth codes `KJV` / `NET`. `LoadAvailableTranslationsAsync` already reads `https://bible.helloao.org/api/available_translations.json`. Hosted JSON on `lumencue-releases` stays. Prefetch still queues whatever the picker offers.

## Goals / Non-Goals

**Goals:** One testable offered-list + alias map used by the cache service and the search seed. Stop constructing `ApiBibleClient` so new desks never hit `/bible/`.

**Non-Goals:** Deleting `ApiBibleClient` or the cloud proxy. Purging local NIV/MSG SQLite rows. Changing Compare or prefetch rules. Adding more helloao English than the booth set.

## Decisions

1. **Static offered list + aliases in Core** (`BiblePickerTranslations` or equivalent) — curated helloao codes, hosted codes, and `KJV→eng_kjv` / `NET→eng_net`. Alternative: keep tuples only on `BibleCacheService` (harder to test the seed without UI).

2. **Apply aliases after the catalog map** — if the catalog already has `KJV`, leave it; otherwise add the alias. Alternative: rename the picker to `KJAV` (fights operator habit and saved `bible_translation=KJV`).

3. **Leave `ApiBibleClient` compiled but unwired** — `App` does not create it; `CombinedBibleService` already accepts null. Alternative: delete the client this change (larger than the picker fix; parser tests still cover MSG grouping for leftover local rows).

## Risks / Trade-offs

- [Desk saved `bible_translation=NIV`] → not in the new list; selection stays `BSB` (existing default). Compare sanitizes saved `MSG,AMP` to empty.
- [Helloao catalog outage] → fallback map MUST still include the alias ids so KJV/BSB/NET can download later.
- [Church expected NIV on a new PC] → they cannot get it; only an already-cached local DB still has those verses.

## Migration Plan

Ship in the next desktop tag. Churches update; picker shows helloao + hosted. Rollback is the previous Velopack version (that version still calls a dead API.Bible key).
