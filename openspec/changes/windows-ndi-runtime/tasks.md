## 1. Failing tests (TDD)

- [x] 1.1 Add `NdiRuntimeLocatorTests` for V6 hit, V5 fallback, and missing file. Verify they fail

## 2. Locator and load

- [x] 2.1 Add `NdiRuntimeLocator` and register a Windows `NDILib` resolver in `NdiOutputService` before initialize. Verify task 1.1 is green

## 3. PATH instead of a second resolver

- [x] 3.1 Add a failing test that the library directory is first on PATH. Verify it fails
- [x] 3.2 Prepend PATH and preload the DLL; do not call `SetDllImportResolver`. Verify task 3.1 is green
