## Context

See proposal.md. `Sender(..., clockVideo: true)` matches NDI’s “clock this sender to the frames” flag: `send` waits until the next frame is due. That wait runs inside `CaptureTick` on `DispatcherPriority.Render`.

## Goals / Non-Goals

**Goals:** Non-blocking `Send`. Keep 15 fps metadata and timer.

**Non-Goals:** Off-thread Skia render (larger change).

## Decisions

1. **`ClockVideo = false` on chrome** — timer is the only pacer. Alternative: background thread (more moving parts, same outcome for the hang).

## Risks / Trade-offs

- [Frames bunch if a tick overruns] → Next tick still 15 Hz; OBS shows last frame. Better than a frozen desk.
