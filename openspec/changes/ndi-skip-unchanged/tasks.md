## 1. Policy

- [x] 1.1 Write failing tests that skip-unchanged is on, static unchanged ticks must not paint, and changed or motion ticks must paint. Verify they fail
- [x] 1.2 Add `NdiCaptureWindowChrome.SkipUnchanged` and `NdiFramePaintPolicy.MustPaint`. Verify task 1.1 is green

## 2. Apply

- [x] 2.1 Wire `CaptureTick` to skip `Render`/`Send` unless `MustPaint` is true, bumping a generation from the program view-model `PropertyChanged`. Verify task 1.1 still passes

## 3. Verify without a Windows booth

- [x] 3.1 Write a failing 10s-static-lyric gate test (150 ticks) plus warmup. Verify it fails
- [x] 3.2 Add `NdiPaintGate` (2 warmup paints, then skip) and log paint/skip counts. Verify task 3.1 is green
