## Why

An operator can fix a typo on a Now Singing slide while that slide is on the projector, save, and the room still sees the old lyrics. The cards rebuild; the projector does not. Mid-service that is the bug: the edit they just made never reaches the screen.

Explored: **A** always push the live slide after save, **B** a Settings toggle, **C** refresh cards only. **Chose A.** No toggle. Same idea as “I meant to fix what is on screen.”

## What Changes

- After a song save (quick-edit, add-slide, or full editor), Now Singing cards reload **and** the LIVE ring stays on the same slide if it still exists.
- If that song’s slide is currently live, the projector SHALL show the new text immediately. No Space/Enter required.
- If a different song is live, or nothing lyrical is live, the projector MUST NOT change.
- If they deleted the slide that was live, do not invent a send. Leave the projector as-is until they send a card.
- **Not in this change:** notes, scripture, a Settings toggle, auto-sending slides that were not live.

## Capabilities

### New Capabilities

- `song-live-sync`: When a saved song edit may rewrite the live lyric slide, and when it must not.

### Modified Capabilities

- None. `openspec/specs/` has no existing capabilities.

## Impact

- New Core policy (`SongLiveSync`) — same family as `TranslationLiveUpdate`
- `SaveSongEditAsync` / full editor save path
- Restore LIVE ring after `OpenSong` rebuild
- Unit tests first; no settings schema
