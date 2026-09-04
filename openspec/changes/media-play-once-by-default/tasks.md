## 1. Failing tests (TDD)

- [x] 1.1 Add `tests/ChurchProjection.Parsing.Tests/AnnouncementPlaybackTests.cs` that pins Media-tab video requests as `Loop: false`, `Audio: true`, with the given path and audio device; pin images as no video request. Verify `dotnet test tests/ChurchProjection.Parsing.Tests --filter AnnouncementPlayback` fails because `AnnouncementPlayback` does not exist
- [x] 1.2 Add a `VideoPlayRequest` test that a request constructed with only a path has `Loop == false`. Verify `dotnet test tests/ChurchProjection.Parsing.Tests --filter VideoPlayRequest` fails while the record default is still `true`
- [x] 1.3 Confirm `BackgroundTilePreviewTests` still expects muted looping previews. Verify `dotnet test tests/ChurchProjection.Parsing.Tests --filter BackgroundTilePreview` passes

## 2. Play-once factory and default

- [x] 2.1 Add `src/ChurchProjection.UI/ViewModels/Operator/AnnouncementPlayback.cs` mirroring `BackgroundTilePreview.RequestFor`: video → `VideoPlayRequest(path, Loop: false, Audio: true, AudioDeviceId)`; image or empty path → null. Verify task 1.1 tests pass
- [x] 2.2 Change `VideoPlayRequest.Loop` default from `true` to `false` in `src/ChurchProjection.UI/Services/Video/IVideoFramePlayer.cs`. Verify task 1.2 tests pass
- [x] 2.3 Point `AnnouncementService` live video start at `AnnouncementPlayback.RequestFor` instead of `new VideoPlayRequest(..., Loop: true, ...)`. Verify `AnnouncementService.cs` has no remaining `Loop: true`

## 3. Background loop regression

- [x] 3.1 Keep `LiveBackgroundService` and `BackgroundTilePreview` on explicit `Loop: true`. Verify `dotnet test tests/ChurchProjection.Parsing.Tests --filter "BackgroundTilePreview|VideoFramePlayerFactory|AnnouncementPlayback"` passes and those two call sites still pass `Loop: true`

## 4. Manual hold-frame check

- [ ] 4.1 On Mac, send a short Media-tab video live, let it finish, and confirm the last frame stays on Program (audio silent) until Clear; send the same clip again and confirm it starts from the beginning. Confirm a video background still loops
