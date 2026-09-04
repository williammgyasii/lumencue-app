## 1. Failing tests (TDD)

- [x] 1.1 Add `ScriptureSearchRankerTests` for: `god so loved the world` → John 3:16 first; phrase hit before a 0.90 semantic-only verse; `god` does not keyword-match `godly`; cosine 0.30 semantic-only is omitted. Verify they fail because the ranker API is missing or still uses the old merge

## 2. Ranker

- [x] 2.1 Implement `ScriptureSearchRanker` (phrase substring, word-boundary keywords, two-band merge, 0.40 semantic floor). Verify task 1.1 is green

## 3. Service

- [x] 3.1 Point `ScriptureSearchService.SearchAsync` at the ranker for tokenize + score + merge. Verify existing `ScriptureParaphraseWatcherTests` still pass and a service compile/test run is green

## 4. Other paths

- [x] 4.1 Re-run `ScriptureReferenceTypedQueryTests` and confirm `John3:16` still parses on the typed path. No ranking code on that path
