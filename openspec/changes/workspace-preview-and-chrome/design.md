## Context

See proposal.md. Today `EnterBibleMode` / `EnterSongsMode` call `ContentSearch.ResetForModeAsync`, which clears the query and loads Genesis 1 or the full song library. `ContentSearch.SelectedItem` then always `SetPreview` and, if `SingleClickGoesLive`, `SendItemToLive`. After a reload the list can auto-select the first row, so a tab click can put Genesis 1:1 or the first song on the projector.

`EnterMediaMode` does not reset the folder (`MediaPlayback.SelectedFolder` already survives). Bookmarks live in the left sidebar; Compare Translations is a Bible-only panel under Program on the 360px right rail; AI Listening is `Grid.Row="3"` across the window. Top tabs use unicode/emoji (`✝`, `♫`, `►`, `📝`) plus a 3px underline.

`LiveClickPolicy` already answers click vs double-click. Settings chrome already lists Material kinds on a Core helper.

## Goals / Non-Goals

**Goals:**

- A testable gate: programmatic / restore selection never goes live
- Session snapshots so Bible / Songs restore instead of reset (Media folder already persists)
- Chrome constants the XAML can bind: Material kinds, full-box active highlight, Compare left, AI right

**Non-Goals:**

- Persist snapshots across app restart
- Change `SingleClickGoesLive` or the Settings Behavior toggle
- Projector / output windows
- New icon packages (Material.Icons.Avalonia is already referenced)
- Workstreams 4–7 (shortcuts, song edit sync, scripture search, lower-third overflow)

## Decisions

1. **Cause on the selection, not a silent flag in the click policy.**
   `WorkspaceSelectionPolicy.MaySendLive(cause, singleClickGoesLive)` returns false for `ModeRestore` and `ListRebuild`, and defers to `LiveClickPolicy` for `OperatorClick`. The `SelectedItem` watcher calls the policy with the cause of that selection. Alternative: stop calling `SendItemToLive` from the watcher entirely (rejected — would break single-click-goes-live on a real list click that arrives through the same property).

2. **Snapshot on leave, restore on enter; first visit keeps today’s default.**
   Before flipping mode flags, save Bible or Songs place (query, browse book/chapter, selected item identity). On enter, restore that snapshot instead of `ResetForModeAsync`. If there is no snapshot, keep Genesis 1 / full song library. Media folder is already on `MediaPlaybackViewModel` — no extra snapshot. Session-only (fields on the view-model). Alternative: persist to settings (rejected — surprise on next Sunday; out of scope).

3. **Chrome policy in Core, same pattern as Settings.**
   `OperatorWorkspaceChrome` lists five nav items (`Label` + Material `IconKind`), active/inactive opacity, `HighlightEntireActiveTab`, `CompareIsSeparateView`, AI tint hexes, `AiListeningUnderProgram`, and `ShowBottomAiBar = false`. XAML uses `{x:Static}` and `Classes.active`. Alternative: XAML-only tweak (rejected — TDD rule).

4. **Full-box highlight replaces the 3px underline.**
   Active tab: filled background + mode-colored border, opacity 1. Inactive: muted fill, opacity ~0.45. Themes stays a ghost-style button (dialog, not a mode). Alternative: keep underline and only gray others (rejected — operator asked to highlight the entire box).

5. **Bookmarks and Compare are sibling left panes; AI fills the right-rail remainder.**
   Same split as Songs library / setlist (`*` / `8` / `*`). Compare is its own view, not nested in Bookmarks. Cards stay one column. AI Listening keeps collapse, mic, transcript, and controls, stacked under Program, with a muted blue panel (`#12202E` / `#2B5A7A`, accent `#38BDF8`) so it reads apart from Program. Bottom AI bar is removed. Center Suggestions tab is unchanged.

Icon kinds (Material): Bible `BookOpenPageVariant`, Songs `MusicNote`, Media `PlayBoxOutline`, Notes `NoteTextOutline`, Themes `PaletteOutline`.

## Risks / Trade-offs

- [Single-click-goes-live via SelectedItem] → Every list selection that should go live MUST be tagged `OperatorClick`. If a click only sets `SelectedItem` with the default cause, live breaks. Mitigation: set cause in the list click path; tests cover both causes.
- [Narrow compare cards] → One column under Bookmarks is readable; two-up is not. Accept the stack.
- [AI panel height] → Right rail under Program is shorter than the old full-width bar. Collapse stays; transcript remains optional.
- [Session-only memory] → Restart still opens Genesis 1 / full library. Acceptable for this change.

## Migration Plan

No persisted data. Rollback is revert the policy, snapshots, and XAML moves.

## Open Questions

None. Active-tab treatment is both (full-box highlight and gray inactive), as requested.
