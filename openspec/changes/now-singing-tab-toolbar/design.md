## Context

See proposal.md. Songs mode already has a top tab strip (`Now Singing` / `Songs`) and a DockPanel title bar inside the Now Singing pane (`NowSingingTitle`, Follow, Lines, +, size). `OperatorWorkspaceChrome` is the existing home for operator layout flags.

## Goals / Non-Goals

**Goals:**

- One Songs chrome row; slide cards do not jump when a song loads
- Title as a Songs-pink pill so it reads as the loaded song
- Same Follow / Lines / + / size commands as today

**Non-Goals:**

- Changing Follow or lines-per-slide behavior
- Bible / Media / Notes tab chrome
- Persisting slide scale

## Decisions

1. **Move the existing stack into the tab strip, do not duplicate it.**
   Tab-strip `Border` becomes a `Grid` (`*` tabs | `Auto` cluster). The inner DockPanel title `Border` is removed. Alternative: leave a thin inner bar (rejected — still jumps).

2. **Policy on `OperatorWorkspaceChrome`.**
   Flags: toolbar-in-tab-strip, no inner title bar, title-is-bubble, bubble colors, hide-on-Songs-tab. Tests encode those. XAML binds `{x:Static}` for colors. Alternative: XAML-only (rejected — TDD rule).

3. **Songs-pink bubble, not purple or amber.**
   Fill `#3B1D3A`, text `#F9A8D4`, border `#F472B6` — same family as the Songs tab underline, distinct from Now Singing purple, Follow amber, and LIVE green. Alternative: reuse `#7C6CF6` (rejected — looks like another tab).

4. **Visibility = Songs mode + Now Singing tab + song loaded.**
   Reuse `ShowNowSinging && HasNowSinging` (or a thin `ShowNowSingingToolbar` alias). Alternative: keep title on the Songs tab (rejected — those controls only apply to slides).

5. **Title ellipsizes; controls stay Auto.**
   Bubble `MaxWidth` (~220) with `CharacterEllipsis` so a long title does not shove Follow off-screen.

## Risks / Trade-offs

- [Narrow window] → Title ellipsizes; controls stay visible.
- [Tab row height] → Match existing tab padding so the row does not grow.
- [WrapPanel cards] → Unchanged; this change only moves chrome.

## Migration Plan

No data migration. Rollback is revert XAML + chrome flags.

## Open Questions

None.
