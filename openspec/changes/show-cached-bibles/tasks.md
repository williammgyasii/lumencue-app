## 1. Merge cached codes

- [x] 1.1 Write a failing test that `WithCached` adds complete `NIV`/`NLT`/`AMP` and leaves them off when the extra cache is empty, then implement `WithCached` until it passes
- [x] 1.2 Point `LoadAvailableTranslationsAsync` at complete `bible_cache_status` rows plus `WithCached`, and re-run the 1.1 tests
