## 1. Failing tests (TDD)

- [x] 1.1 Add `SongLiveSyncTests`: refresh when the saved song is live and the section still exists; skip when another item is live, when the song is not live, or when the live section is gone. Verify the test project fails to compile because `SongLiveSync` does not exist

## 2. Policy and save path

- [x] 2.1 Add `SongLiveSync` in Core (`ShouldRefreshLive` + how to match a rebuilt slide). Verify task 1.1 passes
- [x] 2.2 After `SaveSongEditAsync` rebuild, restore the LIVE ring and re-project when the policy says so. Point the full editor save at the same helper. Verify quick-edit / full-save while that slide is live updates the projector, and a save while scripture is live does not
