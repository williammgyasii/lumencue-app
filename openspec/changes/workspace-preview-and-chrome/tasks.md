## 1. Failing tests (TDD)

- [x] 1.1 Add `WorkspaceSelectionPolicyTests`: `MaySendLive(ModeRestore)` and `MaySendLive(ListRebuild)` are false even when single-click-goes-live is on; `OperatorClick` still follows `LiveClickPolicy`. Verify the test project fails to compile because `WorkspaceSelectionPolicy` does not exist
- [x] 1.2 Add `WorkspaceModeSnapshotTests`: leaving Bible on a non-Genesis chapter and returning restores that chapter; leaving Songs on a search and returning restores that search; first Bible visit may be Genesis 1; Media folder is unchanged after a round-trip. Verify the test fails because the snapshot type does not exist
- [x] 1.3 Add `OperatorWorkspaceChromeTests`: five nav items with labels + Material kinds (no emoji); `HighlightEntireActiveTab` is true; inactive opacity is below 1; Compare is under Bookmarks; AI Listening is under Program; bottom AI bar is off. Verify the test fails because `OperatorWorkspaceChrome` does not exist

## 2. Policy and session memory

- [x] 2.1 Add `WorkspaceSelectionPolicy` in Core. Gate `ContentSearch.SelectedItem` so restore/rebuild never calls `SendItemToLive`. Operator clicks still use `LiveClickPolicy`. Verify task 1.1 passes
- [x] 2.2 Snapshot Bible/Songs place on leave and restore on enter instead of `ResetForModeAsync`. First visit keeps Genesis 1 / full song library. Media folder stays on `MediaPlayback`. Verify task 1.2 passes

## 3. Operator chrome

- [x] 3.1 Add `OperatorWorkspaceChrome` (nav items, opacities, layout flags) in Core. Verify task 1.3 passes
- [x] 3.2 Apply chrome in `OperatorWindow.axaml`: Material icons on the five top tabs, full-box active highlight and gray inactive tabs, Compare under Bookmarks (Bible-only, one column), AI Listening under Program, remove the bottom AI bar. Verify the window builds and the layout matches the chrome flags
- [x] 3.3 Split Bookmarks and Compare into two sibling left views; tint the AI Listening panel (`#1A1730` / `#4C3F6E`). Verify `OperatorWorkspaceChromeTests` pass
