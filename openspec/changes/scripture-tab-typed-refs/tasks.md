## 1. Failing tests (TDD)

- [x] 1.1 Add parser tests for `John3:16`, `I John 3:16`, `1st John 3:16`, `John 3:16,18`, and `John 3:16-4:2` via the typed expander. Verify they fail because that API does not exist or does not return those slices. Confirm default `TryParse("John3:16")` still fails (spoken stays strict)

## 2. Parser

- [x] 2.1 Implement `TryParseTypedQuery` (compact space, I/1st prefixes, comma verses, cross-chapter slices). Verify task 1.1 passes and existing fuzzy / `John 3:16` tests still pass

## 3. Scripture-tab search

- [x] 3.1 In `SearchScripturesAsync`, if the typed expander returns slices, fetch each with `GetOrFetchVersesAsync` and concatenate. Verify a search-level test (or parser+fetch fixture) returns both verses for `John 3:16,18` and spans John 3–4 for `John 3:16-4:2`, and that phrase search still runs for non-references
