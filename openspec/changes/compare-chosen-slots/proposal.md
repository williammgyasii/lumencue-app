## Why

Compare’s cog is supposed to pick extra translations (max 2) beside the one already live. Today `ForDisplay` auto-fills empty slots and the refresh **writes that back** as the saved picks. Uncheck one and another appears. The default (`MSG,AMP`) is not even in the picker. Operators cannot toggle what they want.

## What Changes

Explored: **A** chosen checkboxes are the source of truth — 0, 1, or 2 cards, no auto-fill (chosen), **B** keep auto-fill on cards only, **C** two dropdowns. **A** matches “1 pick → 1 card, 2 picks → 2 cards.”

- Checkboxes stay toggleable up to 2
- Cards shown = chosen translations minus the active/live translation
- Empty slots stay empty
- Saved picks are not overwritten by auto-fill
- Cog copy says “up to 2,” not “exactly 2”

**Not in this change:** Compare pane placement, compare-card send-live, translation-live-update preference, ranking.

## Capabilities

### New Capabilities

- `compare-chosen-slots`: How many Compare cards appear from the operator’s picks, and that the active translation is not a compare card.

### Modified Capabilities

- None. Workspace chrome only places Compare on the left. `translation-live-update` still treats a compare-card send as an explicit send.

## Impact

- `LiveCompareSelection.ForDisplay` (drop available fill)
- `OperatorViewModel.RefreshLiveCompareAsync` (do not write display codes back into `_compareChosen`)
- Default / sanitize saved codes against the picker list
- Cog flyout copy
