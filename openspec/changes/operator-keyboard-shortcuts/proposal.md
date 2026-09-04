## Why

Sunday operators already have arrows, Space/Enter, and Esc on the operator window, but they still reach for the mouse to change mode, toggle Output, or open Settings. Those actions have no keys and no on-screen map, so the existing live console is easy to forget and incomplete.

Explored: **A** polish-only (document current keys), **B** a small fixed live map plus `?` cheatsheet, **C** remappable Settings keymap. **Chose B.** Numbers avoid colliding with search typing if focus leaks; remapping is a later change.

## What Changes

- Keep today’s live keys: Left/Right page live, Up/Down only in Now Singing / open note, Space/Enter send preview live, Esc blanks (or clears a focused text box).
- Add a **fixed** operator map when the operator window is focused and the source is **not** a text box:
  - `1` Bible · `2` Songs · `3` Media · `4` Notes (top-row and numpad)
  - `O` toggle Output
  - `,` open Settings
  - `?` show/hide a cheatsheet overlay
- Keys MUST NOT fire while typing. No remapping, no global OS hotkeys, no projector-window bindings, no Themes / AI Listening / Start keys in this change.

## Capabilities

### New Capabilities

- `operator-shortcuts`: Fixed operator-window keymap, text-box exclusion, and cheatsheet overlay.

### Modified Capabilities

- None. `openspec/specs/` has no existing capabilities.

## Impact

- New Core policy (`OperatorShortcuts`) mapping key → action; unit tests first
- `OperatorWindow` tunnel/bubble handlers execute the actions
- Cheatsheet overlay on the operator window (session toggle, not persisted)
- Mode switches reuse `EnterBibleMode` / `EnterSongsMode` / `EnterMediaMode` / `EnterNotesMode` (preview-only; must not send live)
- Output reuses `ToggleScreenOutputCommand`; Settings reuses `OnSettingsClicked`
- No settings schema, no persisted keymap, no projector / Theme Studio changes
