## Why

Booths run LumenCue and OBS on one PC. NDI still paints a full 1920×1080 Skia frame 15 times a second on the UI thread even when the lyric has not moved, so the whole computer freezes. Lyrics sit still most of a service.

## What Changes

Explored: **A** skip paint/send when the program frame is unchanged (chosen), **B** helper process for NDI, **C** send 720p or move OBS to a second PC. **B** does not unfreeze the machine (same CPU/RAM). **C** fights the same-computer product and the full-canvas capture spec. 15 fps, hidden 1920×1080 window, and `clockVideo: false` stay.

- Do not Skia-paint or NDI-send when program content is unchanged and no motion background is live
- Paint and send when the slide/theme/layers change, or when a video background is playing
- First frame after NDI start still paints

**Not in this change:** a second process, shrinking the capture window, changing fps metadata.

## Capabilities

### New Capabilities

- `ndi-skip-unchanged`: When the NDI capture tick may skip painting.

### Modified Capabilities

- None. `ndi-capture-15fps`, `ndi-hidden-capture-window`, and `ndi-send-nonblocking` stay.

## Impact

- `NdiCaptureWindowChrome` skip flag
- `NdiFramePaintPolicy` in Core
- `NdiOutputService.CaptureTick`
