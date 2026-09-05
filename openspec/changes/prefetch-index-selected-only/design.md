## Context

See proposal.md. `CacheTranslationAsync` always called `EnsureIndexedAsync` after `EnsureTranslationCachedAsync`. Prefetch reuses that method for every picker code.

`bible-picker-prefetch` queue order is unchanged.

## Goals / Non-Goals

**Goals:** Gate indexing with a testable selected-vs-cached compare.

**Non-Goals:** Changing MiniLM, disk index format, or NDI.

## Decisions

1. **`BiblePrefetchIndex.ShouldIndex(cached, selected)` in Core** — case-insensitive, false when either side is blank. Alternative: a boolean `indexAfterCache` on the prefetch loop (easy to miss the switch-translation path).

2. **Keep one `CacheTranslationAsync`** — switch-translation already passes the new selected code, so the same gate stays true.

## Risks / Trade-offs

- [Find Scripture on a just-switched unindexed Bible] → same as before prefetch: index starts on switch via `CacheTranslationAsync`.
- [Selected changes mid-prefetch] → a later queue item that matches the new selected will index; earlier ones will not. Acceptable.
