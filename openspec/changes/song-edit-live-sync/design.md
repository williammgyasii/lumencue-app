## Context

See proposal.md. `SaveSongEditAsync` persists, refreshes the library, and `OpenSong` rebuilds Now Singing. `OpenSong` clears follow state and LIVE rings. It never calls `SendItemToLive`. The full editor’s `Saved` handler only calls `RefreshLibraryAsync`, so even the cards can stay stale.

`TranslationLiveUpdate.ShouldRefreshLive` is the existing “may I rewrite what’s live?” gate. Lyric identity after rebuild is a section that still exists (same section type + label, else same index if the count is unchanged).

## Goals / Non-Goals

**Goals:**

- One testable policy: given “this song is live” + “this section still exists” → refresh live or not
- All three save paths call that policy
- Rebuild restores the LIVE ring when the section still exists

**Non-Goals:**

- Settings toggle
- Notes / scripture
- Sending a neighbor slide when the live section is gone

## Decisions

1. **Policy in Core, same shape as translation.**
   `SongLiveSync.ShouldRefreshLive(savedSongIsLive, liveSectionStillExists)` is true only when both are true. The view-model re-sends that slide when true. Alternative: always `SendSlideLive` after save (rejected — would steal the projector from scripture / another song).

2. **Identity is the live section, not “first slide.”**
   Remember which Now Singing card was live (section type + label, fallback index). After rebuild, find that card. Missing → do not send. Alternative: always re-send index 0 (rejected — would jump the room to verse 1).

3. **Full editor save goes through `SaveSongEditAsync` (or the same helper).**
   Today `Saved += RefreshLibraryAsync` is not enough. Alternative: only fix quick-edit (rejected — full editor is the bigger typo path).

## Risks / Trade-offs

- [Split a section into two slides] → Old label may not match. Fallback index can refresh the wrong half. Prefer label match; if both fail, do not send.
- [Lines-per-slide change] → Pagination changes; index fallback is unsafe. If label match fails, do not send.
- [Operator editing a live slide they did not mean to push] → A is the Sunday trade-off they asked for.

## Migration Plan

No persisted data. Rollback is revert the policy and the save-path hook.

## Open Questions

None for the default. Immediate rewrite of the matching live slide (option A).
