## Context

See proposal.md. `scripture-slide-grid` already fixed the Scripture tab with WrapPanel + `OperatorWorkspaceChrome.ScriptureCardWidth` + `ContentItem.CardWidth` stamped from list `SizeChanged`. Find Scripture is a second `ListBox` (`TopicalListBox`) with `Width="212"` and no hug item styles. Design of that change listed Find Scripture as a non-goal.

## Goals / Non-Goals

**Goals:**

- Same width math: `ScriptureCardWidth(topicalListWidth)`
- Stamp `CardWidth` on topical `Results` the way `ContentSearch` does
- WrapPanel + top-aligned items; bind width on the card item

**Non-Goals:**

- Changing `ScriptureCardWidth` itself
- Detected / paraphrase list
- Ranking or search query behavior

## Decisions

1. **Reuse `ScriptureCardWidth`, do not invent a second formula.** Same padding, gutter, slack, min width. Alternative: share `ContentSearch.CardWidth` (rejected — topical list is a different pane).

2. **`TopicalSearchViewModel.SetCardPaneWidth`** mirrors `ContentSearchViewModel`. Window `SizeChanged` and topical list `SizeChanged` both call it.

3. **Chrome flags** `FindScriptureCardsHugContent` / `FindScriptureUsesWrapPanel` so the policy is testable without XAML.

## Risks / Trade-offs

- [Search box sits above the list] → width is measured on `TopicalListBox`, not the whole tab, so the three columns still fill the cards area.
- [Narrow window] → same min width (~120) as Scripture; may wrap to fewer visual columns.
