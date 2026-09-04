## Context

See proposal.md for why. `ScriptureSearchService.SearchAsync` already tokenizes, LIKE-searches SQLite, then merges MiniLM cosine. Keyword score is `0.55 * tokenFraction` (cap 0.55). Semantic-only cosine is used raw (0.25+), so a loose embedding can outrank an exact phrase. `scripture-tab-typed-refs` requires Find Scripture to stay topical/hybrid; this change only reorders that merge.

## Goals / Non-Goals

**Goals:**

- Pure ranker in Core so fixtures do not need embeddings or SQLite
- Phrase containment and word-boundary keywords outrank semantic-only
- Raise the semantic floor from 0.25 to 0.40
- Service keeps fetching keyword candidates and (when ready) semantic candidates; ranker decides order

**Non-Goals:**

- New embedding model or on-disk index format
- BM25 / FTS rebuild
- Changing `TryParseTypedQuery` or spoken `TryParse`
- Changing how `TopicalSearchViewModel` renders cards

## Decisions

1. **Extract `ScriptureSearchRanker` in Core** (same pattern as `SongLiveSync` / `OperatorShortcuts`).
   - Alternatives: retune constants inside `ScriptureSearchService` (hard to unit-test without ONNX), or add FTS5 (new index, bigger change).
   - Ranker owns tokenize, phrase detect, keyword score, merge, floor.

2. **Phrase = normalized substring.** Lowercase both sides, collapse whitespace, strip simple punctuation. If the full query (after normalize) appears in the verse, it is a phrase hit. Stronger than token overlap.

3. **Keyword = word-boundary, not LIKE.** SQL can still retrieve with `LIKE %token%`. Ranker re-scores with a word-boundary check so `god` does not match `godly`.

4. **Two-band sort.** Band 0 = phrase or keyword hit. Band 1 = semantic-only at/above 0.40. Sort by band, then by score. Semantic-only never sits above band 0.

5. **Spoken watcher unchanged.** It already calls `SearchAsync`. Better ranking may improve detections; no watcher edits.

## Risks / Trade-offs

- [Paraphrases with no shared wording drop in rank] → Semantic-only still appears, just below keyword/phrase hits and only at cosine ≥ 0.40.
- [SQL still returns substring candidates] → Ranker filters false keyword matches; extra rows are cheap at `maxResults * 3`.
- [Index still incomplete on first search] → Keyword/phrase band works without embeddings, same as today.
