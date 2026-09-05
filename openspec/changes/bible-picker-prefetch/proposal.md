## Why

Switching translations often shows “not available yet” because only the selected Bible is cached. Compare cards and live refresh then miss text. The booth should already be downloading every picker Bible in the background.

## What Changes

Explored: **A** background queue of picker Bibles, selected first (chosen), **B** all ~49 English helloao translations, **C** chapter-on-demand only. **A** matches the desk picker. Chapter fetch stays so the open verse can fill before the whole book is done.

- After translations load, enqueue the selected code then every picker code
- Download one at a time (existing `EnsureTranslationCachedAsync`)
- Do not enqueue helloao English codes that are not in the picker
- Selected may be outside the picker (e.g. BSB) and MUST still be first

**Not in this change:** API.Bible outage handling, Find Scripture ranking, NDI.

## Capabilities

### New Capabilities

- `bible-picker-prefetch`: Background download order for Bibles the operator can switch to.

### Modified Capabilities

- None. No archived bible-cache spec.

## Impact

- `BibleCacheQueue` in Core
- `BibleCacheService` walks the queue
- `OperatorViewModel` starts the queue after the picker list loads
