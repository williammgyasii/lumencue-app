## Why

The Scripture tab uses a 3-column `UniformGrid`, so one or two verse cards stretch as tall as the pane. Compact typing (`John3` on the way to `John3:16`) also parses as a whole chapter, so the grid jumps chapter → empty → one giant card. Cards must fill the row width without stretching tall.

## What Changes

Explored: wrap-only (hug height, ragged widths), keep UniformGrid and only top-align (still tall cells), **3 equal columns that fill width + hug height**, and treat compact-without-colon as still typing. The last pair was chosen. Scripture tab only.

- Scripture cards sit in three columns that fill the list width. Card height hugs the 16:9 preview + label bar.
- Compact input with digits and **no colon** (`John3`) is a partial reference: show no cards, do not load the chapter, do not run phrase search.
- `John 3` (space, no compact glue) still loads the chapter. `John3:16` still loads that verse.
- Notes tab UniformGrid is out of scope.

## Capabilities

### New Capabilities

- `scripture-slide-grid`: Scripture card column fill + hug height; compact-without-colon is partial.

### Modified Capabilities

- None.

## Impact

- `OperatorWorkspaceChrome` column/width math and hug/wrap flags
- `OperatorWindow.axaml` Scripture `ListBox` panel + card width
- `ContentSearchViewModel` (or operator VM) card width
- `ScriptureReferenceParser.TryParseTypedQuery` / `LooksLikePartialReference`
- Tests next to chrome + typed-query tests
