## Why

With NDI on, LumenCue paints a full 1920×1080 Skia frame 30 times a second on the UI thread. Booth PCs sit around 60% CPU. Lyrics do not need 30 fps.

## What Changes

Explored: **A** cap NDI capture at 15 fps (chosen), **B** slow Bible prefetch, **C** shrink the in-memory topical index. **A** is the steady CPU hog when NDI is on. Hide-window rules stay (full canvas, opacity 0, not minimized).

- Capture timer and NDI `VideoFrame` clock at 15 fps
- Do not change window size, opacity, or prefetch

**Not in this change:** skip-unchanged frames, prefetch throttle, MiniLM memory.

## Capabilities

### New Capabilities

- `ndi-capture-15fps`: How often the NDI program feed is painted and sent.

### Modified Capabilities

- None. `ndi-hidden-capture-window` size/visibility requirements stay.

## Impact

- `NdiCaptureWindowChrome` fps constants
- `NdiOutputService` timer and `VideoFrame` use those constants
