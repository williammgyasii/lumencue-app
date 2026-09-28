## Why

v0.7.39 hid NIV, NLT, and AMP from the picker after API.Bible was cancelled. Churches that already downloaded those Bibles still have the verses in SQLite, but cannot select them. Pastor David hit that on the booth tonight.

## What Changes

Explored: **A** union the picker with locally complete caches (chosen), **B** dump NIV onto GitHub Releases (rejected — redistribution), **C** re-subscribe to API.Bible just to fill David (slower; his disk likely already has the text).

- Picker still lists helloao + hosted JSON
- Picker also lists any translation whose local cache is complete (NIV, NLT, AMP, …)
- Incomplete or unknown codes stay off the picker
- Seed before catalog load stays helloao + hosted (no paid codes until we know the disk)
- Do not re-wire API.Bible or host new JSON this change

**Not in this change:** downloading NIV for a wiped PC, GitHub export, Compare rules.

## Capabilities

### New Capabilities

- `show-cached-bibles`: Locally finished translations appear on the operator picker.

### Modified Capabilities

- None. `helloao-only-bibles` still defines the downloadable set. This adds cached extras; it does not put NIV back on the download list.

## Impact

- `BiblePickerTranslations.WithCached`
- `BibleCacheService.LoadAvailableTranslationsAsync` reads `bible_cache_status`
- Tests for merge of offered + complete codes
