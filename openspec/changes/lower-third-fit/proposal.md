## Why

On a Lower Third theme, long verses can paint outside the bottom band. `DeckBuilder` pages against the body box, but the live text grid is not clipped, and measurement can disagree with `OutlinedTextBlock`. Mid-service that looks like a broken graphic.

## What Changes

Explored: **A** page so each slide fits the body box, then clip the region (chosen), **B** AutoFit-shrink only, **C** clip/ellipsis only. **A** keeps type readable and never draws outside the band.

- A verse that fits the lower-third body stays one slide
- A verse that does not fit MUST become more than one slide
- Each page’s measured body MUST fit the usable body box (width/height minus padding)
- Title / body / footer regions MUST clip their contents

**Not in this change:** Theme Studio editor, imported-graphic placement, STT copy.

## Capabilities

### New Capabilities

- `lower-third-fit`: How scripture (and other body text) pages and clips inside a Lower Third body box.

### Modified Capabilities

- None.

## Impact

- `DeckBuilder` pagination vs default Lower Third region
- `ProjectorView` region `ClipToBounds`
- New deck-fit tests
