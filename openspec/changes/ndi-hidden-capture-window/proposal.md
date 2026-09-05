## Why

Turning NDI on shows a 1920×1080 borderless window titled `LumenCue NDI` over the church Windows desktop. Avalonia must attach the program feed to a shown window or frames go black. We park it at `(-20000, -20000)`; Windows snaps off-screen windows back on-screen.

## What Changes

Explored: **A** keep the full-size window, set opacity 0, do not activate, do not hit-test (chosen), **B** minimize (black frames), **C** shrink to 1×1 (clipped/black). **A** keeps `RenderTargetBitmap` painting the capture root at canvas size.

- Capture window stays 1920×1080, not minimized
- Operator cannot see it (opacity 0) and it does not steal focus
- If Windows still snaps it on-screen, it does not eat clicks
- NDI frames still come from rendering the capture root, not a screenshot of the HWND

**Not in this change:** NDI library load (`windows-ndi-runtime`), OBS/DistroAV, source name, Mac-only workarounds.

## Capabilities

### New Capabilities

- `ndi-hidden-capture-window`: How the NDI capture window stays paintable without covering or blocking the operator desktop.

### Modified Capabilities

- None. No archived NDI capture-window spec.

## Impact

- `NdiCaptureWindowChrome` in Core (testable constants)
- `NdiOutputService` applies those constants when it creates the capture window
