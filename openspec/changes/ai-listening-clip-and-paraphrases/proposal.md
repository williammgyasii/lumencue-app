## Why

The right-rail AI Listening panel grows past its slot, so transcript and controls paint over the rest of the operator window. The transcript box clips instead of scrolling, so new words disappear. The AI Suggestions tab still shows a Start Listening button when the mic is off. The Paraphrases subtab is parked until we rebuild that feature.

Approved as one workstream: clip + mini-scroll, hide the off-state start button, remove the Paraphrases tab from the UI.

## What Changes

- AI Listening stays inside the right-rail remainder under Program. It clips and scrolls; it MUST NOT overflow the rail.
- The live transcript is a fixed-height mini-scroll. New text pushes older lines up; the view sticks to the latest words.
- AI Suggestions (the listening results tab) hides Start Listening / “AI Listening is off” when the mic is off. It shows the scripture suggestion list only. Start/Stop stays on the right-rail panel.
- The Paraphrases subtab and its content are hidden. Detection code may stay; we will rebuild the feature later.
- No change to bookmarks, Find Scripture, or how listening starts from the right rail.

## Capabilities

### New Capabilities

- `ai-listening-panel`: Right-rail clip/scroll, transcript stick-to-latest, suggestions off-state, Paraphrases tab hidden.

### Modified Capabilities

- None. `openspec/specs/` has no existing capabilities.

## Impact

- `OperatorWorkspaceChrome` flags and transcript height
- `OperatorWindow.axaml` AI rail, Suggestions banner, Paraphrases tab/content
- `OperatorViewModel` visibility for the start prompt and Paraphrases tab
- Transcript scroll-to-end in `OperatorWindow` code-behind
- Unit tests next to `OperatorWorkspaceChromeTests`
- Watcher / paraphrase engine left in place (no UI)
