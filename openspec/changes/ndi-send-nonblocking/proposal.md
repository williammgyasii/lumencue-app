## Why

0.7.36 dropped NDI to 15 fps but left `clockVideo: true`. NDI then **blocks** `Send` on the UI thread until the 15 fps clock elapses (~66 ms) after each 1080p paint. The desk stops processing input. No exception, so logs stay clean. Memory (~1 GB) is unrelated (ONNX + windows).

## What Changes

Explored: **A** turn off NDI video clocking and let the DispatcherTimer pace frames (chosen), **B** move capture to a worker thread, **C** skip unchanged slides. **A** is the 0.7.36 regression. 15 fps and the hidden full-canvas window stay.

- `clockVideo` is false so `Send` does not wait
- Timer still fires at 15 fps; the VideoFrame still advertises 15 fps

**Not in this change:** prefetch throttle, RAM, changing fps again.

## Capabilities

### New Capabilities

- `ndi-send-nonblocking`: NDI send must not clock/block the operator UI thread.

### Modified Capabilities

- None. `ndi-capture-15fps` and `ndi-hidden-capture-window` stay.

## Impact

- `NdiCaptureWindowChrome.ClockVideo`
- `NdiOutputService` passes it into `Sender`
