## 1. Failing tests (TDD)

- [x] 1.1 Add chrome tests for hug-height, wrap panel, 3 columns, and `ScriptureCardWidth` third-of-pane. Add parser tests that `John3` is partial / empty typed slices and `John 3` still parses as a chapter. Verify they fail

## 2. Policy

- [x] 2.1 Add chrome flags and `ScriptureCardWidth` on `OperatorWorkspaceChrome`. Treat compact-without-colon as partial in `LooksLikePartialReference` and `TryParseTypedQuery`. Verify task 1.1 passes

## 3. Scripture UI

- [x] 3.1 WrapPanel + top-aligned items; bind card width; update width from the list size. Verify the window builds
