## Context

See proposal.md. `OperatorViewModel` only calls `CacheTranslationAsync` for the selected code. `BibleCacheService.EnsureTranslationCachedAsync` already downloads one translation (free complete.json, hosted JSON, or API.Bible chapters) and no-ops when complete.

## Goals / Non-Goals

**Goals:** Order the picker downloads and start that queue after sign-in / settings load.

**Non-Goals:** Changing chapter-on-demand, downloading all 49 helloao English, fixing API.Bible `ServiceUnavailable`.

## Decisions

1. **`BibleCacheQueue.Order(selected, offered)` in Core** — selected first (if non-empty), then offered, case-insensitive unique. Alternative: picker only (drops BSB when it is selected but not listed).

2. **Reuse `EnsureTranslationCachedAsync` one at a time** — already skips in-flight and complete caches. Alternative: parallel API.Bible walks (rate-limit risk).

3. **Operator starts `EnsureOfferedTranslationsCachedAsync` once** after the picker list is loaded, instead of only caching the selected code. Switching still calls `CacheTranslationAsync` so the new pick jumps the line if it was not finished.

## Risks / Trade-offs

- [Long first-run download] → Status line already reports `Downloading {code}...`. Selected finishes first.
- [API.Bible failures] → Same as today for that code; others in the queue still run.
