## Context

See proposal.md. `SearchScripturesAsync` calls `TryParse(query, allowFuzzyBook: true)` then one `GetOrFetchVersesAsync`. `ScriptureReference` is one book + one chapter + verse span. Phrase search runs only when parse fails.

Spoken/AI uses `TryParse` without fuzzy and `ExtractFromSpoken`. Those must not call the new typed expander.

## Goals / Non-Goals

**Goals:**

- One typed expander: query → zero or more `ScriptureReference` slices
- Scripture-tab search fetches every slice and concatenates
- Tests pin the four query shapes plus existing `John 3:16` / fuzzy cases

**Non-Goals:**

- Find Scripture / embedding index
- Changing `TryParse` default (spoken) behavior
- Multi-book ranges (`John 3:16-Acts 2:1`)
- Changing how cards go live

## Decisions

1. **New typed entry point, do not overload spoken `TryParse`.**
   `TryParseTypedQuery(string)` (name may vary) returns `IReadOnlyList<ScriptureReference>`. Empty list → existing phrase / partial-reference path. Alternative: grow `TryParse` (rejected — spoken would inherit comma/cross-chapter and invent refs from speech).

2. **Keep one chapter per `ScriptureReference`.**
   `John 3:16-4:2` becomes two slices: `John 3:16`–end (sentinel 200) and `John 4:1-2`. `GetOrFetchVersesAsync` already filters one chapter. Alternative: add EndChapter to the record (rejected — ripples cache keys and API fetch).

3. **Comma list = one slice per verse (or per contiguous run).**
   `John 3:16,18` → `(John,3,16)` and `(John,3,18)`. Search concatenates in typed order. Alternative: one range 16–18 (rejected — would include 17, which was not typed).

4. **Normalize numbered prefixes before book resolve.**
   Leading `I`/`II`/`III` and `1st`/`2nd`/`3rd` (with optional space) map to `1`/`2`/`3` then existing `NormalizeBook` / fuzzy. Only on the typed expander.

5. **Optional space between book and chapter on the typed expander only.**
   `John3:16` and `1John3:16` match the same verse patterns after a thin normalize (`John 3:16`). Do not change `LooksLikePartialReference` mid-type behavior for `mat `.

6. **`SearchScripturesAsync` uses the expander first.**
   If any slices parse, fetch each (existing `GetOrFetchVersesAsync`) and return the combined list. Do not also run phrase search. Invalid-reference notice still applies when every slice misses.

## Risks / Trade-offs

- [`love` / `job` as words] → Expander still requires a chapter number; bare words stay phrase search.
- [Psalm 119 after 3:16 in a cross-chapter] → Sentinel 200 already means “rest of chapter”; fetch is per-chapter.
- [II John vs “ii” typo] → Prefix normalize is only at the start of the book token.

## Migration Plan

No persisted data. Rollback is revert parser + `SearchScripturesAsync` branch.

## Open Questions

None.
