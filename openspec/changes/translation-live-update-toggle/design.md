## Context

See proposal.md. Today `ContentSearch.SelectedTranslation` changes fire `OnTranslationChangedAsync`, which always reloads the chapter grid and then `RefreshLiveTranslationAsync` re-projects live scripture. That last step is the surprise.

`SingleClickGoesLive` is the existing Settings → Behavior bool: load via `GetBoolAsync(key, default)`, persist in the property setter.

## Goals / Non-Goals

**Goals:**

- Gate only the live re-project on a persisted bool (default true)
- Keep library/search translation switch even when the gate is off
- Show the toggle in Settings → Behavior

**Non-Goals:**

- No confirmation dialog
- No per-verse or per-translation override
- No change to compare-card "send this translation live"
- No change to how a verse is first sent live

## Decisions

1. **Policy helper in Core, not a dialog.**
   `TranslationLiveUpdate.ShouldRefreshLive(enabled, scriptureIsLive)` is the testable gate. The view-model skips `RefreshLiveTranslationAsync` when it returns false. Library reload and live-highlight relink still run.
   Alternatives: confirmation dialog (rejected); skip the entire `OnTranslationChangedAsync` when off (would leave the library in the old translation).

2. **Default on, key `update_live_on_translation_change`.**
   Matches current operators. Same persist pattern as `single_click_goes_live`.

3. **Compare-card send is not gated.**
   Double-clicking a compare card is an explicit send, not a picker change.

## Risks / Trade-offs

- [Live ring vs output mismatch] → With the toggle off, the library card can show a live ring on the new-translation text while Program still shows the old translation. Acceptable: the ring means "this verse reference is on screen," not "this wording is on screen."
- [Forgotten off] → Operator changes translation expecting live to follow. They send the verse again, or turn the setting back on.

## Migration Plan

No data migration. Missing key = on. Rollback is removing the gate and the Settings row.

## Open Questions

None.
