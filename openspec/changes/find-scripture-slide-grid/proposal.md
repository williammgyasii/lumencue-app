## Why

Find Scripture result cards sit in a WrapPanel at a fixed **212px** width. The Scripture tab already fills three columns and hugs height. Resize on Find Scripture leaves a ragged gap; short result sets can stretch. Same desk, two grid rules.

## What Changes

Explored: **A** reuse Scripture-tab chrome (`ScriptureCardWidth`, hug, WrapPanel) on Find Scripture results (chosen), **B** bind to `ContentSearch.CardWidth`, **C** a different column count. **A** keeps one width formula.

- Find Scripture result cards SHALL use the same three-column fill + hug-height rule as the Scripture tab
- Card width SHALL come from `ScriptureCardWidth` of the topical list pane, restamped on resize
- Detected / paraphrase list stays a stacked lane (not this grid)

**Not in this change:** ranking, typed Scripture-tab refs, detected-lane layout, compact `John3` partials.

## Capabilities

### New Capabilities

- `find-scripture-slide-grid`: Find Scripture result cards fill three columns and hug height, matching the Scripture tab.

### Modified Capabilities

- None. `scripture-slide-grid` stays Scripture-tab-only. This is the same visual rule on a different list.

## Impact

- Chrome flags for Find Scripture hug / wrap
- `TopicalSearchViewModel` stamps `ContentItem.CardWidth`
- `TopicalListBox` SizeChanged + window resize
- Find Scripture card template binds `Width` and top-aligns items
