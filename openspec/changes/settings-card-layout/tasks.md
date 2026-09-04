## 1. Failing tests (TDD)

- [x] 1.1 Update `SettingsFormChromeTests` so pane fit, 200/40 columns, stacked screens, and nav icon+label fail because those chrome members do not exist yet

## 2. Chrome policy

- [x] 2.1 Add pane math, column widths, stacked-screens flag, and nav items on `SettingsFormChrome`. Verify task 1.1 passes

## 3. Settings UI

- [x] 3.1 Apply chrome numbers in `SettingsWindow.axaml`: fill the pane, card rows, `compactSwitch`, stacked screen cards, sidebar icons. Verify the window builds and no control is wider than the content pane
- [x] 3.2 Put Screens back on one row (switch + name + theme). Verify `ScreensUseSingleRow` and the row still fits `ContentWidth`
- [x] 3.3 Cap page ScrollViewer at `ContentPaneHeight` so Screens (and other tabs) scroll inside the dialog
