## Why

Settings rows overflow the fixed 780×620 dialog: the form max-width (560) plus padding is wider than the content pane, and Fluent `ToggleSwitch` will not shrink. Operators see clipped controls and a layout that fights itself.

Explored: (1) fill the pane with SettingsCard-style rows + compact switches + nav icons — chosen, matches Windows Settings cards and Apple’s fixed preferences window; (2) widen the window so 560 fits — still overflows on Fluent switches and the Screens table; (3) paid Actipro SettingsCard — extra dependency for one dialog.

## What Changes

- Settings content fills the pane (`window − sidebar − padding`) and MUST NOT exceed it.
- Display / Behavior / About rows become SettingsCard-style grids: text in `1fr`, control in a reserved column.
- Behavior (and Screens on/off) use the existing 40×22 `compactSwitch`, not Fluent `ToggleSwitch`.
- Screens stay one row: compact switch + name + theme combo (same as before, but the switch is 40px so the row fits).
- Sidebar items show a Material icon plus label.
- Window stays fixed 780×620, not resizable.

## Capabilities

### New Capabilities

- `settings-layout`: How the Settings dialog sizes itself and lays out rows so nothing overflows.

### Modified Capabilities

- None. `openspec/specs/` has no existing settings-layout capability.

## Impact

- `SettingsFormChrome` (pane math, column widths)
- `SettingsWindow.axaml` / code-behind
- Reuse `compactSwitch` and Material icons already in the operator window
- `SettingsFormChromeTests`
