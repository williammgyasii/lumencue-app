## 1. Gate

- [x] 1.1 Write failing tests for follow-on not indexed and selected indexed. Verify they fail because `BiblePrefetchIndex` is missing
- [x] 1.2 Add `BiblePrefetchIndex.ShouldIndex` and verify task 1.1 is green

## 2. Apply

- [x] 2.1 Call the gate in `CacheTranslationAsync` before `EnsureIndexedAsync` and verify selected still indexes
