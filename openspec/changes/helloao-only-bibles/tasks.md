## 1. Offered list and aliases

- [x] 1.1 Write a failing test that the picker offered codes are the helloao booth set plus hosted JSON and exclude NIV-family codes, then add `BiblePickerTranslations` until it passes
- [x] 1.2 Write a failing test that a catalog of `KJAV→eng_kjv` and `NETB→eng_net` still resolves `KJV`/`NET` as bulk-cacheable, then apply aliases after the catalog map until it passes

## 2. Wire the desk

- [x] 2.1 Point `BibleCacheService` at `BiblePickerTranslations` (offered list, catalog aliases, fallback ids) and re-run the tests from 1.1 and 1.2
- [x] 2.2 Write a failing test that a new `ContentSearchViewModel` seed includes `KJV`/`BSB` and excludes `NIV`/`MSG`, then change the seed until it passes
- [x] 2.3 Stop constructing `ApiBibleClient` in `App.axaml.cs` and verify that file no longer new-s the client (CombinedBibleService already accepts null)
