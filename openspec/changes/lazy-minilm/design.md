## Context

See proposal.md. Splash starts `SemanticEmbeddingService.InitializeAsync` on a thread. `CacheTranslationAsync` then indexes the selected translation (~31k verses + ONNX session).

`SearchAsync` already calls `EnsureIndexedAsync`. Keyword search works without embeddings.

## Goals / Non-Goals

**Goals:** Policy flags + wire startup/cache off. Init inside `EnsureIndexedAsync` so the first search still works.

**Non-Goals:** Compressing the on-disk `.vec` file or shrinking NDI buffers.

## Decisions

1. **Policy constants in Core** — same chrome pattern. Alternative: delete the calls with no testable rule.

2. **Init on first `EnsureIndexedAsync`** — so Find Scripture still gets MiniLM. Alternative: never load (kills semantic search).

## Risks / Trade-offs

- [First Find Scripture is slower] → one-time ONNX + index. Keyword hits still return immediately.
