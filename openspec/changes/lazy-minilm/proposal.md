## Why

Booths sit near 1 GB RAM because MiniLM/ONNX loads on every launch and then the selected Bible is fully indexed after prefetch. Most services never open Find Scripture.

## What Changes

Explored: **A** load MiniLM only when topical search actually runs (chosen), **B** drop verse objects from the in-memory index, **C** unload ONNX after each search. **A** is the idle-RAM win and stays compatible with keyword search.

- Do not initialize embeddings at splash
- Do not start topical index after a Bible cache
- First Find Scripture search still builds/loads the index (existing `SearchAsync` call)

**Not in this change:** NDI send, prefetch download order.

## Capabilities

### New Capabilities

- `lazy-minilm`: When the embedding engine and topical index may start.

### Modified Capabilities

- None.

## Impact

- `SemanticEmbeddingPolicy` in Core
- `App.axaml.cs` startup
- `OperatorViewModel.CacheTranslationAsync`
- `ScriptureSearchService.EnsureIndexedAsync` initializes on demand
