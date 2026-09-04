## Context

See proposal.md. `OperatorWindow` already tunnels Left/Right (and Up/Down in Now Singing / open note) so lists cannot steal paging. Bubble `OnKeyDown` runs Space/Enter (`TransitionCommand`) and Esc (`BlankCommand`, or clear the focused `TextBox`). There is no mode / Output / Settings / cheatsheet map.

`ChurchProjection.Core` has no Avalonia reference. Mode entry (`EnterBibleMode` and siblings) already restores session place and must not send live (`WorkspaceSelectionPolicy`). Output is `ToggleScreenOutputCommand`. Settings is `OnSettingsClicked` (owned dialog).

## Goals / Non-Goals

**Goals:**

- One testable Core lookup: key token + modifiers + “is text input” + “cheatsheet visible” → action
- Window executes that action; existing live commands stay the implementation
- Cheatsheet rows live on a Core chrome list so XAML can bind them

**Non-Goals:**

- Remappable keys or a Settings keymap editor
- Global hotkeys when the app is in the background
- Projector / Theme Studio / AI Listening / Start keys
- Persisting cheatsheet visibility

## Decisions

1. **Core tokens, not Avalonia `Key`.**
   `OperatorShortcuts.Resolve(token, shift, ctrl, alt, meta, isTextInput, cheatsheetVisible)` returns an `OperatorShortcutAction`. The window maps `Key.D1` / `NumPad1` → `"1"`, `Key.O` → `"O"`, `Key.OemComma` → `","`, `Key.OemQuestion` or (`Key.Oem2` + Shift) → `"?"`. Alternative: put the map in code-behind (rejected — TDD rule; Core stays UI-free).

2. **One table for old and new keys.**
   Actions include `None`, `Bible`, `Songs`, `Media`, `Notes`, `ToggleOutput`, `OpenSettings`, `ToggleCheatsheet`, `DismissCheatsheet`, `SendLive`, `Blank`, `PageForward`, `PageBack`. Up/Down stay in the existing tunnel (context-sensitive); the policy does not need them. Alternative: only add the new keys and leave Space/Esc duplicated (rejected — Esc vs cheatsheet needs the policy).

3. **Text box and modifiers fail closed.**
   Any `isTextInput` → `None` (window still clears the box on Esc, as today). Digit / letter / comma actions require no modifiers. `?` allows Shift only. Ctrl / Cmd / Alt never match. Alternative: fire mode keys even while typing (rejected — `1 John` would leave Bible search).

4. **Mode keys call `Enter*Mode`, not a new path.**
   Those already restore place and refuse live on rebuild. The shortcut MUST NOT call `SendItemToLive`. Alternative: set `SelectedContentTab` only (rejected — would skip restore).

5. **Cheatsheet is a session bool + overlay.**
   `ShowShortcutCheatsheet` on the operator VM. Overlay lists `OperatorShortcutChrome.Rows` (key label + action label). Esc while visible is `DismissCheatsheet`, not `Blank`. Hidden on restart. Alternative: Settings cheat sheet page (rejected — operator asked for `?` on the console).

## Risks / Trade-offs

- [Layout-specific `?`] → US is Shift+`/`. Map both `OemQuestion` and `Oem2`+Shift. Other layouts may need a later tweak.
- [`,` vs locale] → OemComma is the physical comma key. Accept US-first.
- [Settings from a key] → `OnSettingsClicked` today uses the cog as owner for tooltip hold. Key-open can pass the window as owner.
- [Modal Settings] → While Settings is open the operator window will not see keys. Acceptable.

## Migration Plan

No persisted data. Rollback is revert the policy, handler cases, and overlay.

## Open Questions

None. Map is option B as chosen: `1`–`4`, `O`, `,`, `?`.
