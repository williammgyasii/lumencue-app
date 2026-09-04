## Why

Media-tab videos always start in continuous loop, so a bumper, announcement, or worship clip restarts on its own during a live event. Operators then have to stop or clear it by hand. Event clips should play once and stay on the last frame unless the operator sends something else.

## What Changes

- Media-tab video (and video-with-audio) sent live plays **once**, then **holds the last frame**. It does not restart.
- Still images are unchanged (they have no timeline).
- Live backgrounds and background tile previews **keep looping**. Those are motion beds, not event clips.
- No loop toggle in this change. Operators who want a clip to repeat send it again, or we add a toggle in a later change.
- **Not breaking** for persisted libraries: loop is a playback request, not a saved asset field.

## Capabilities

### New Capabilities

- `media-playback`: Rules for when a live video loops versus plays once, by playback role (Media-tab announcement vs live background vs thumbnail preview).

### Modified Capabilities

- None. `openspec/specs/` has no existing capabilities.

## Impact

- `AnnouncementService` live video start (currently hard-codes `Loop: true`)
- `VideoPlayRequest` default (`Loop = true` today)
- Native players already honor `Loop: false` (AVFoundation holds the last frame; LibVLC omits `:input-repeat`)
- `LiveBackgroundService` and `BackgroundTilePreview` stay on explicit `Loop: true`
- New unit tests next to `BackgroundTilePreviewTests` / `VideoFramePlayerFactoryTests`
- No settings schema, library JSON, or audio-device changes
