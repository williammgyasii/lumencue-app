## Context

See proposal.md. `CaptureTick` always `Render`s 1920×1080 then `Send`s. `ndi-capture-15fps` already named skip-unchanged as the next change. The timer and VideoFrame stay at 15 fps so OBS still sees that rate; idle ticks do no Skia work. Hidden full-canvas window and `ClockVideo = false` stay.

Avalonia `Render` must run on the window's dispatcher. Skip must happen **before** `Render`, not by hashing pixels after paint.

## Goals / Non-Goals

**Goals:** Testable paint policy + chrome flag. Wire `CaptureTick` so static lyrics do not paint.

**Non-Goals:** Helper process, 720p send, changing fps constants, resend-keepalive of the last buffer.

## Decisions

1. **`NdiFramePaintPolicy.MustPaint(skip, contentChanged, motionLive)`** — same chrome/policy pattern as `ClockVideo`. Alternative: hash the bitmap after `Render` (still pays Skia).

2. **Content generation from `ProjectorViewModel.PropertyChanged`** — slide, theme, layers, announcements, and live-background `Frame` already raise it. Video backgrounds therefore keep 15 fps paint without a separate motion detector. `motionLive` remains on the policy so a still generation with an explicit motion flag still paints.

3. **No send when skipping** — OBS holds the last frame. Alternative: resend the previous buffer at 1 Hz (follow-up if a receiver drops the source).

## Risks / Trade-offs

- [Some NDI tools drop a silent source] → first frame still sends; churches can toggle NDI off/on. Keepalive is a later delta.
- [Motion still costs 15 fps 1080p + OBS] → expected; skip cannot help a playing video.
- [Missed PropertyChanged] → first start still paints; next slide/theme change raises the VM.

## Migration Plan

Ship in the next desktop tag. Churches update and toggle NDI off/on. Rollback is the previous Velopack version.
