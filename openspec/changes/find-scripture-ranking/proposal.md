## Why

The **Find Scripture** tab ranks topical/phrase queries by a hybrid of keyword LIKE and MiniLM cosine. Semantic-only hits can score 0.6–0.9 while an exact phrase match is capped at 0.55, so a famous line like “god so loved the world” can lose to a loosely related verse. Mid-service that looks like a broken index.

## What Changes

Explored: **A** keyword/phrase-first merge + higher cosine floor + fixture tests (chosen), **B** full BM25 + embedding rebuild, **C** fold Find Scripture into the Scripture-tab box. **A** keeps hybrid search and fixes order without a new index.

- A verse whose text contains the query phrase, or a word-boundary keyword match, MUST rank above every semantic-only hit
- Famous-phrase fixture: `god so loved the world` MUST return John 3:16 first when that verse is in the candidate set
- Semantic-only hits below a higher cosine floor MUST drop
- Find Scripture stays topical/hybrid (typed Scripture-tab refs and spoken `TryParse` stay as they are)

**Not in this change:** typed Scripture-tab parser, spoken/AI reference expansion, embedding rebuild, lower-third overflow, BM25.

## Capabilities

### New Capabilities

- `find-scripture-ranking`: How Find Scripture orders topical/phrase hits when keyword, phrase, and semantic scores compete.

### Modified Capabilities

- None. `openspec/specs/` has no archived capability for this. `scripture-tab-typed-refs` already requires Find Scripture to stay topical/hybrid; this change keeps that true.

## Impact

- New `ScriptureSearchRanker` in Core (tokenize, phrase/keyword score, merge)
- `ScriptureSearchService.SearchAsync` uses the ranker
- New ranker tests next to other Core service tests
- `ScriptureParaphraseWatcher` still calls `SearchAsync`; no watcher or spoken-parser edits
