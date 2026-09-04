## Context

See proposal.md. AI Listening is a star-row under the 16:9 Program monitor. A `ScrollViewer` wrapping a tall `StackPanel` still overflows because the star child will not shrink below its desired height. The transcript is a 90px `TextBlock` with `VerticalAlignment="Bottom"` and `ClipToBounds`, so extra lines vanish. AI Suggestions shows Start Listening when `!IsListening`. Paraphrases is content-tab index 4.

## Goals / Non-Goals

**Goals:**

- Chrome flags a unit test can encode (clip, transcript scroll, hide start, hide paraphrases)
- Rail body uses a `*` row + `MinHeight="0"` so it actually scrolls
- Transcript `ScrollViewer` sticks to the end when `Transcript` changes
- Suggestions off-banner gone; Paraphrases tab/content gone from XAML visibility

**Non-Goals:**

- Delete `ScriptureParaphraseWatcher` or detection tests
- Change right-rail Start/Stop
- Rebuild paraphrases

## Decisions

1. **Flags on `OperatorWorkspaceChrome`, same as the last chrome change.**
   `AiListeningClipsToRail`, `TranscriptScrollsInsideBox`, `TranscriptBoxHeight`, `TranscriptStickToLatest`, `SuggestionsTabShowsStartWhenOff = false`, `ShowParaphrasesSubtab = false`. Alternative: a new helper type (rejected — same panel family).

2. **Star-row shrink + inner scroll.**
   AI border `MinHeight="0"` and a header/`*` grid so Avalonia can clip. Alternative: cap Program height (rejected — 16:9 monitor stays).

3. **Stick-to-end in the window, not the VM.**
   `RecentTranscript` already tails 700 characters. Code-behind sets the transcript `ScrollViewer` offset after layout. Alternative: only `VerticalAlignment="Bottom"` (rejected — that clips, which is today’s bug).

4. **Hide Paraphrases in chrome + visibility, keep the watcher.**
   If `SelectedContentTab` is the paraphrases index while the tab is hidden, snap to Scripture. Alternative: delete the watcher now (rejected — rebuild later).

## Risks / Trade-offs

- [Program 16:9 eats the rail] → AI slot can be short; inner scroll is the mitigation.
- [User scrolled the transcript up] → New text jumps them back to the end. Acceptable for a live feed.
- [Tab index 4 leftover] → Snap to Scripture so the center pane is not blank.

## Migration Plan

No persisted data. Rollback is revert chrome flags and XAML.

## Open Questions

None. Scope matches the approved list.
