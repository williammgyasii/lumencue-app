## 1. Failing tests (TDD)

- [x] 1.1 Add `TranslationLiveUpdateTests` that `ShouldRefreshLive(enabled: true, scriptureIsLive: true)` is true, and that enabled-false or scripture-not-live is false. Verify the test project fails to compile because `TranslationLiveUpdate` does not exist

## 2. Policy and setting

- [x] 2.1 Add `TranslationLiveUpdate` in Core (`ShouldRefreshLive`, settings key, default true). Verify task 1.1 passes
- [x] 2.2 Add `UpdateLiveOnTranslationChange` on `OperatorViewModel`, load/save like `SingleClickGoesLive`, default true. Gate `RefreshLiveTranslationAsync` in `OnTranslationChangedAsync`

## 3. Settings UI

- [x] 3.1 Add a Behavior-tab row: "Update live scripture when I change translation" bound to the new property. Verify the row sits with the other workflow toggles
