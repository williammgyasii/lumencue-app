## Context

See proposal.md. Today `SettingsFormChrome.ContentMaxWidth` is 560 inside a 780 window with a 216 sidebar and 56px padding (508 usable). Fluent `ToggleSwitch` still overflows. `compactSwitch` (40×22) already exists on the operator window.

## Goals / Non-Goals

**Goals:**

- Pane math that cannot overflow (`ContentWidth <= Window − Sidebar − PaddingX`)
- Shared card row: `*, {combo|toggle}` with wrap on hints
- Reuse `compactSwitch` and Material icons
- Screens as one row: compact switch + name + theme

**Non-Goals:**

- Resizable Settings
- Actipro or other new packages
- Changing operator-window density / Fluent theme globally
- New settings or copy rewrites beyond layout

## Decisions

1. **Fill the pane, do not cap above it.**
   `ContentWidth = WindowWidth − SidebarWidth − ContentPaddingX`. XAML `MaxWidth` binds to that. Alternative: widen the window (rejected — switches and the Screens table still overflow).

2. **Policy in `SettingsFormChrome`, not magic numbers in XAML.**
   Tests encode pane fit, column widths, single-row screens, and nav icon+label. XAML uses `{x:Static}`. Alternative: UI-only tweak with no tests (rejected — TDD rule).

3. **Copy `compactSwitch` styles into SettingsWindow.**
   Operator styles are window-local. Duplicating ~30 lines avoids a shared resource hunt. Alternative: move styles to App.axaml (out of scope).

4. **Nav icons as chrome data, not only XAML.**
   `SettingsFormChrome.NavItems` lists label + Material kind so the “icon + label” scenario is testable. XAML binds or repeats the same five items.

## Risks / Trade-offs

- [Duplicated compactSwitch] → Accept for this change; extract later if a third surface needs it.
- [Long OS/runtime strings on About] → Hint/value wrap; reserved column stays Auto for read-only values, still inside ContentWidth.
- [Screens name field is narrower] → Compact switch (40) + 160 theme leaves the rest for the name; still inside ContentWidth.

## Migration Plan

No data migration. Rollback is revert XAML + chrome constants.

## Open Questions

None.
