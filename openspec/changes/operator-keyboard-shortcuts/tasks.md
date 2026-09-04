## 1. Failing tests (TDD)

- [x] 1.1 Add `OperatorShortcutsTests` that encode: `2` → Songs, `1` → Bible (not SendLive), numpad `3` → Media, `O` → ToggleOutput, `,` → OpenSettings, `?` toggles cheatsheet, text-input or Shift+`1` → None, Space → SendLive, Esc with cheatsheet visible → DismissCheatsheet. Verify the test project fails to compile because `OperatorShortcuts` does not exist
- [x] 1.2 Add `OperatorShortcutChromeTests` that `Rows` lists Bible, Songs, Media, Notes, Output, Settings, send live, blank, and page live. Verify it fails because `OperatorShortcutChrome` does not exist

## 2. Policy and chrome

- [x] 2.1 Add `OperatorShortcuts` in Core (`Resolve` + `OperatorShortcutAction`). Verify task 1.1 passes
- [x] 2.2 Add `OperatorShortcutChrome.Rows` (key label + action label). Verify task 1.2 passes

## 3. Operator window

- [x] 3.1 Map Avalonia keys to tokens in `OperatorWindow` and execute the resolved action (`Enter*Mode`, Output, Settings, cheatsheet, existing Transition/Blank/page). Verify digits / `O` / `,` / `?` do nothing while a TextBox is focused
- [x] 3.2 Add `ShowShortcutCheatsheet` on the operator VM and an overlay bound to `OperatorShortcutChrome.Rows`. Verify `?` shows then hides it, and Esc hides it without blanking
