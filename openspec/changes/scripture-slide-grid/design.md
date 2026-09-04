## Context

See proposal.md. Scripture `ListBox` uses `UniformGrid Columns="3"` and stretch. Cards have a fixed 118px preview and no `CardWidth`. `TryParseTypedQuery` inserts a space (`John3` → `John 3`) and then chapter-parses.

## Goals / Non-Goals

**Goals:**

- Width math: `(pane − padding − gutters) / 3`
- Wrap panel + top-aligned items + bound card width
- Compact-without-colon is partial (typed expander empty + `LooksLikePartialReference`)

**Non-Goals:**

- Notes grid
- Changing `John 3` chapter search
- Find Scripture tab

## Decisions

1. **WrapPanel + computed third-width, not UniformGrid.**
   UniformGrid always shares leftover **height**. WrapPanel hugs rows. Width comes from `OperatorWorkspaceChrome.ScriptureCardWidth(paneWidth)`. Alternative: UniformGrid + Top (rejected — cells still fill the pane).

2. **Card width on `ContentSearchViewModel`, updated from list `SizeChanged`.**
   Same pattern as Program preview cap in the window code-behind. XAML binds `Width` on the card panel.

3. **Detect compact-partial on the raw string, before space insertion.**
   Book letters (or numbered book) immediately followed by digits, no `:` → partial. `TryParseTypedQuery` returns []. `LooksLikePartialReference` returns true so phrase search does not run.

## Risks / Trade-offs

- [Narrow window] → floor card width (~120) so text still fits; may wrap to fewer visual columns.
- [`1John3` mid-type] → same compact-partial rule.

## Migration Plan

No data. Rollback is revert chrome, XAML, and the compact-partial check.

## Open Questions

None.
