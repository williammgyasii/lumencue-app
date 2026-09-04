## 1. Failing tests (TDD)

- [x] 1.1 Change `ForDisplay` tests so one chosen code shows one card, two show two, available does not fill, and uncheck stays at one. Verify the fill test fails on the old auto-pad behavior

## 2. Selection

- [x] 2.1 Remove available-fill from `ForDisplay`. Stop `RefreshLiveCompareAsync` from writing display codes back into `_compareChosen`. Sanitize saved picks to the picker list. Verify task 1.1 is green

## 3. Cog copy

- [x] 3.1 Change the Compare cog to “up to 2” (not “exactly 2”). Verify the window builds
