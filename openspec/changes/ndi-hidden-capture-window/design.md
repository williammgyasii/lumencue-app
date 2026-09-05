## Context

See proposal.md. `NdiOutputService.Start` creates a real Avalonia `Window`, shows it, and each tick does `_captureBitmap.Render(_captureRoot)`. Frames are not a screenshot of the HWND, so window opacity does not blank the NDI feed.

`windows-ndi-runtime` stays the library-load change. This change only covers capture-window chrome.

## Goals / Non-Goals

**Goals:** Encode hide-without-blanking as testable chrome constants and apply them when the capture window is created.

**Non-Goals:** Changing how frames are copied, NDI library load, or a Win32 `WS_EX_TRANSPARENT` interop layer unless Avalonia hit-test is not enough later.

## Decisions

1. **`NdiCaptureWindowChrome` in Core** — same pattern as `SettingsFormChrome`. Tests encode WHEN/THEN without constructing a live window. Alternative: headless Avalonia window tests (heavier, flaky on CI).

2. **Opacity 0 + ShowActivated false + IsHitTestVisible false** — chosen option A. Alternative: minimize or 1×1 (spec forbids; those blank or clip frames).

3. **Keep `Position = (-20000, -20000)`** — still try to stay off-screen on Mac. Windows may snap; chrome then hides and click-throughs. Alternative: skip Show() (black frames).

## Risks / Trade-offs

- [Windows still paints a ghost HWND] → Opacity 0. If a compositor ignores it, we still have no-activate + no hit-test.
- [IsHitTestVisible does not make the native HWND click-through] → Follow-up Win32 layered/transparent styles; do not shrink the window.
- [Churches on 0.7.32] → Needs a Velopack update (`v0.7.33`).

## Migration Plan

Ship in the next app tag. Churches install the update, toggle NDI off/on. Rollback is the previous Velopack version.
