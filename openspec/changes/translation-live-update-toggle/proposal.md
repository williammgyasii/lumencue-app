## Why

Changing the Bible translation today always re-renders whatever scripture is already live. That is useful mid-service when the operator meant to switch versions, and surprising when they only wanted a different translation for search or the next verse. Operators need a setting, not a confirmation dialog.

## What Changes

- Settings → Behavior gets a toggle: **Update live scripture when I change translation**.
- **On** (default): today's behavior — live scripture re-renders in the new translation.
- **Off**: the picker, search, and library switch translation; the verse already on screen stays in the translation it was sent in until the operator sends a verse again.
- No confirmation prompt. Preference persists like the other Behavior toggles.
- **Not breaking**: default on matches current operators.

## Capabilities

### New Capabilities

- `translation-live-update`: When a translation change may rewrite live scripture, and when it must not.

### Modified Capabilities

- None. `openspec/specs/` has no existing translation-live-update capability.

## Impact

- `OperatorViewModel.OnTranslationChangedAsync` / `RefreshLiveTranslationAsync` (gate the live re-project)
- Settings Behavior tab + `SettingsRepository` bool key
- Unit tests for the gate policy
- Library/search translation change and compare-card "send this translation live" stay as they are
