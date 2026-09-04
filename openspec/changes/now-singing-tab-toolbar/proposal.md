## Why

Opening a song adds a second chrome row (title + Follow / Lines / + / size) under the Now Singing / Songs tabs. The slide grid jumps down, so the pane feels unstable. The title also reads as another toolbar label instead of “this is the loaded song.”

## What Changes

Explored: **A** title-only on the tab row (controls stay below), **B** glue the title onto the Now Singing tab label, **C** one chrome row — title + controls on the far right of the tab strip. **C** was chosen. Song title is a distinct colored bubble so it stands out from the tabs.

- When a song is open on the Now Singing tab, title + Follow + Lines + add-slide + size sit on the **same row as the tabs**, right-aligned.
- The inner title bar under the tabs is removed so the slide grid top edge does not move when a song loads.
- The song title is a pill/bubble with a Songs-pink fill and text, not plain white toolbar copy.
- The cluster is hidden on the Songs search tab and when no song is loaded.
- Follow, Lines, add-slide, and size behavior stay the same.

## Capabilities

### New Capabilities

- `now-singing-tab-toolbar`: One-row Songs chrome; title bubble; no inner title bar.

### Modified Capabilities

- None. `openspec/specs/` has no existing capability for this.

## Impact

- `OperatorWorkspaceChrome` flags and title-bubble colors
- `OperatorWindow.axaml` Songs tab strip and Now Singing DockPanel
- Optional `ShowNowSingingToolbar` on `OperatorViewModel`
- `OperatorWorkspaceChromeTests`
- Card wrap/hug from the prior Now Singing card fix stays; Bible / Media / Notes chrome is out of scope
