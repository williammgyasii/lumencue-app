## Context

See proposal.md. `LiveCompareSelection.ForDisplay` currently pads to two from `available`, then `RefreshLiveCompareAsync` copies that list onto `_compareChosen` and rebuilds the checkboxes. Tests encode the old fill (`ForDisplay_FillsTheEmptySlotWhenACardGoesLive`). Default save is `MSG,AMP`, which is not in `AvailableTranslations`.

## Goals / Non-Goals

**Goals:**

- `ForDisplay(chosen, live)` returns chosen minus live, never pads
- Refresh does not mutate `_compareChosen`
- Sanitize saved codes to the picker list; empty default
- Cog copy: up to 2

**Non-Goals:**

- Changing `MaxSlots`
- Changing `SendCompareLive`
- Moving Compare off the left pane

## Decisions

1. **Drop the `available` fill path.** Alternative: keep fill for cards only (rejected — user cannot have one card).

2. **Sanitize on load.** Drop codes not in `AvailableTranslations` so leftover `MSG,AMP` does not sit invisibly under empty checkboxes.

3. **Keep skip-live in `ForDisplay`.** Chosen can still include the active translation; that card is omitted so the pane stays “aside” the one in use.

## Risks / Trade-offs

- [First launch shows no cards] → Empty state tells them to use the cog. Better than a fake pair.
- [Live skip leaves 0 cards when they only picked the active one] → Correct; they already see that text on the main stage.
