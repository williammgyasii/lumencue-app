## Why

0.7.34 prefetches every picker Bible, then starts Find Scripture MiniLM indexing after each download. That was fine when only the selected Bible cached. Thirteen full-Bible indexes peg a booth PC and feel like a freeze.

## What Changes

Explored: **A** prefetch still downloads; only the selected translation is indexed (chosen), **B** also change NDI opacity hide, **C** roll back to 0.7.32. **A** matches the prefetch spec (texts only) and the pre-0.7.34 index behavior.

- After a cache completes, start topical index only when that code is the selected translation
- Switching translations still indexes the new pick (same helper, selected == cached)
- Do not change download order or NDI

**Not in this change:** NDI capture chrome, stopping prefetch downloads.

## Capabilities

### New Capabilities

- `prefetch-index-selected-only`: When prefetch may start the topical search index.

### Modified Capabilities

- None. Queue order stays in `bible-picker-prefetch`.

## Impact

- Small Core helper for the index gate
- `OperatorViewModel.CacheTranslationAsync` uses it
