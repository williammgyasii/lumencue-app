## Why

Switching Bible / Songs / Media / Notes can send the first list row live and wipe the other mode’s place (Genesis 1, full song library). Operators treat tabs as navigation. The top bar still uses emoji/unicode glyphs, Compare sits under Program, and AI Listening is a full-width bottom bar — so the active workspace is hard to see and the right rail fights the left.

## What Changes

Explored: **A** block live-on-tab-only, **B** remember-mode-only, **C** both. **C** was chosen. Operator chrome for this workspace is in the same change (not a second workstream).

- Tab switch, list rebuild, and mode restore MUST NOT send anything live. Preview of the restored item is allowed. Live stays on explicit send (click policy / double-click).
- Each mode remembers its session place: Bible keeps chapter/search, Songs keeps search/open song, Media keeps folder. Switching is navigation, not a reset.
- Top workspace buttons use Material icons (same family as Settings / cog). No emoji or dingbat glyphs.
- Active workspace tab highlights the **entire** button; inactive tabs are grayed.
- Compare Scriptures moves **left as its own view**, sibling to Bookmarks (Bible-only).
- AI Listening moves **right, under Program**. The bottom AI bar is removed.
- Not breaking for libraries or click-policy settings. Session memory is in-memory only (not persisted across restart).

## Capabilities

### New Capabilities

- `workspace-mode-memory`: When a selection may go live, and what each workspace mode restores after a tab switch.
- `operator-workspace-chrome`: Top-nav icons, active-tab highlight, Compare under Bookmarks, AI Listening under Program.

### Modified Capabilities

- None. `openspec/specs/` has no existing capabilities for this.

## Impact

- `OperatorViewModel.EnterBibleMode` / `EnterSongsMode` (today call `ContentSearch.ResetForModeAsync`)
- `ContentSearch.SelectedItem` subscription (today `SetPreview` + `SendItemToLive` when `SingleClickGoesLive`)
- `ContentSearchViewModel.ResetForModeAsync` (wipes query; Genesis 1 or full song library)
- `OperatorWindow.axaml` top nav, left sidebar Bookmarks, right-rail Compare, Grid.Row 3 AI bar
- New Core policy + chrome helpers and tests (same pattern as `LiveClickPolicy` / `SettingsFormChrome`)
- Projector / output windows, click-policy setting, and workstreams 4–7 are out of scope
