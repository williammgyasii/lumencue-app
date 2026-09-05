## 1. Policy

- [x] 1.1 Write failing tests that launch load and post-cache index are off. Verify they fail
- [x] 1.2 Add `SemanticEmbeddingPolicy` and verify task 1.1 is green

## 2. Apply

- [x] 2.1 Stop splash `InitializeAsync` and post-cache `EnsureIndexedAsync`; init inside `EnsureIndexedAsync` when indexing actually runs
