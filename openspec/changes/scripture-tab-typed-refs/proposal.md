## Why

The Bible **Scripture tab** search box misses or incompletes some typed references. They fail to parse, fall through to phrase search, and show the wrong verses — or only part of the range. Mid-service that looks like a broken index.

## What Changes

Explored: **A** fixture-driven typed-reference parse + full fetch, **B** retune hybrid ranking, **C** merge Find Scripture into one box. **A** was chosen. Scripture tab only.

Typed queries that SHALL parse as references and return **every** named verse:

- Compact: `John3:16` (no space after the book)
- Numbered-book prefixes: `I John 3:16`, `1st John 3:16` (and II/2nd, III/3rd)
- Same-chapter list: `John 3:16,18`
- Cross-chapter range: `John 3:16-4:2`

Existing parses stay (`John 3:16`, `John 3`, `mat 1 1`, fuzzy typos). Phrase / topical search is unchanged when the text is not a reference.

**Not in this change:** Find Scripture tab, spoken/AI matching, embedding rebuild, ranking weights.

## Capabilities

### New Capabilities

- `scripture-tab-typed-refs`: Which typed Scripture-tab strings are references, and that the result set is the full named passage.

### Modified Capabilities

- None. `openspec/specs/` has no existing capability for this.

## Impact

- `ScriptureReferenceParser` (typed path only)
- `ContentLibraryService.SearchScripturesAsync` (fetch each parsed slice)
- New parser / search tests next to `ScriptureReferenceFuzzyTests`
- Spoken `TryParse` / `ExtractFromSpoken` stay strict
