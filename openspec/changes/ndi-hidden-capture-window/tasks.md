## 1. Hidden chrome

- [x] 1.1 Write failing tests for opacity 0, no activate, no taskbar, and no hit-test; confirm they fail because `NdiCaptureWindowChrome` is missing
- [x] 1.2 Add `NdiCaptureWindowChrome` with those constants and verify task 1.1 is green

## 2. Full canvas

- [x] 2.1 Write a failing test that width/height match the program canvas and the window is not minimized; confirm it fails
- [x] 2.2 Complete those chrome constants and verify task 2.1 is green

## 3. Apply

- [x] 3.1 Apply `NdiCaptureWindowChrome` on the NDI capture window in `NdiOutputService.Start` and verify a test that the service window uses those constants (or the chrome tests plus the wired assignments)
