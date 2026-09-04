## Context

See proposal.md for why event clips must stop looping. Today every `VideoPlayRequest` defaults to `Loop: true`, and `AnnouncementService` also passes `Loop: true` explicitly. Native players already honor `Loop: false`: AVFoundation uses `AVPlayerActionAtItemEndNone` (hold last frame) and only seeks to zero when looping; LibVLC adds `:input-repeat=65535` only when looping.

`BackgroundTilePreview.RequestFor` is the existing pattern for a testable play-request factory. Live backgrounds already have tests that pin `Loop: true`.

## Goals / Non-Goals

**Goals:**

- Make Media-tab live video requests `Loop: false` through a testable factory, mirroring `BackgroundTilePreview`
- Keep live-background and thumbnail requests on explicit `Loop: true`
- Align the `VideoPlayRequest` default with the safer event-clip behavior so a new caller cannot silently loop

**Non-Goals:**

- No operator Loop toggle or per-asset saved preference
- No native player or LibVLC option changes (play-once + hold last frame already works)
- No change to transport (pause, seek, skip, volume) or media routing
- No change to still-image handling

## Decisions

1. **Role-specific request factory, not a saved field on `AnnouncementMedia`.**
   Loop is a playback role, not an asset property. Persist nothing. Add `AnnouncementPlayback.RequestFor(item, audioDeviceId)` next to `BackgroundTilePreview.RequestFor`, returning `Loop: false`, `Audio: true`.
   Alternatives: store `Loop` on each library item (overkill, needs migration); a global settings toggle (not in this change).

2. **Change `VideoPlayRequest.Loop` default from `true` to `false`.**
   New call sites become event-safe unless they opt into looping. Existing background/preview callers already pass `Loop: true` explicitly (and tests pin that).
   Alternatives: leave the default `true` and only change `AnnouncementService` (works, but the type default would still be the unsafe one).

3. **Hold last frame by relying on existing decoder behavior.**
   Do not add an end-of-clip handler that seeks, blanks, or pauses in C#. AVFoundation already holds; LibVLC without `:input-repeat` stops on the last frame.
   Alternatives: pause at `Position = 1` from C# (extra moving parts, easy to fight the decoder).

## Risks / Trade-offs

- [LibVLC last-frame fidelity] → Some LibVLC builds may show black after end instead of the last frame. Accept that on Windows/Linux for this change; Mac (AVFoundation) is the hold-frame guarantee. Revisit only if a later report shows black-after-end on LibVLC.
- [Operators who liked looping bumpers] → They re-send the clip. A toggle can be a later change.
- [Default flip on `VideoPlayRequest`] → Audit every construction site in this change; backgrounds must keep passing `Loop: true`.

## Migration Plan

No data migration. Deploy is a normal app update. Rollback is reverting the request `Loop` flags and the record default.

## Open Questions

None. End-of-clip hold vs blank was decided as hold last frame before this design.
