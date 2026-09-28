## Context

See proposal.md. `LoadAvailableTranslationsAsync` returns only `BiblePickerTranslations.Offered`. `bible_cache_status.is_complete` already records NIV/NLT/AMP on desks that fetched them before 0.7.39. Prefetch no-ops when a code is already complete.

## Goals / Non-Goals

**Goals:** One testable merge of offered codes plus complete cache codes, with display names for the paid leftovers David uses.

**Non-Goals:** Re-wiring `ApiBibleClient`. Uploading JSON. Purging or re-downloading caches. Changing the first-paint seed.

## Decisions

1. **`WithCached(completeCodes)` on `BiblePickerTranslations`** — offered first, then complete codes not already listed. Display name from a small known-name map, else the code itself. Alternative: query scriptures for distinct translations (slower; status table is the source of “finished”).

2. **`LoadAvailableTranslationsAsync` reads complete rows after the helloao catalog** — same return type the operator already binds. Alternative: a second UI list (more chrome tonight).

## Risks / Trade-offs

- [David’s PC never finished the NIV cache] → picker still omits it; next step is re-enable the API.Bible download while the key lives.
- [Complete flag with missing verses] → same as today; we trust `is_complete`.

## Migration Plan

Ship the next desktop tag. David updates. Rollback is 0.7.39.
