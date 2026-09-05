## Context

See proposal.md. `NdiOutputService` currently hardcodes `FpsNumerator = 30` for both the `DispatcherTimer` and `VideoFrame`. `ndi-hidden-capture-window` only covers visibility/size.

## Goals / Non-Goals

**Goals:** One testable fps constant used by timer and NDI clock.

**Non-Goals:** Adaptive fps, dirty-rect skip, changing canvas size.

## Decisions

1. **15 fps on `NdiCaptureWindowChrome`** — same chrome type the service already reads. Alternative: a second clock class (split for no gain).

2. **Keep sending every tick** — OBS/DistroAV stay fed. Alternative: skip unchanged slides (more savings, separate change).

## Risks / Trade-offs

- [Motion backgrounds look slightly less smooth over NDI] → Lyrics stay sharp; video is 15 fps. Acceptable for church IMAG into OBS.
- [Some NDI tools assume 30] → Clock the VideoFrame at 15 so receivers know the rate.
