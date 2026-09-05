## Context

See proposal.md. `NDILibDotNetCoreBase` P/Invokes `NDILib`. NDI Tools/Runtime on Windows ships `Processing.NDI.Lib.x64.dll` and sets `NDI_RUNTIME_DIR_V6` (or V5). The Windows publish does not copy that DLL. Probe fails with `DllNotFoundException`; the Screens toggle can still look on.

## Goals / Non-Goals

**Goals:** Find the installed Windows runtime and load it under the `NDILib` name before initialize.

**Non-Goals:** Shipping the NDI binary, changing Mac load, changing the OBS source name.

## Decisions

1. **`NdiRuntimeLocator` in Core** — given env dirs and extra roots, return the first existing `Processing.NDI.Lib.x64.dll`. V6 before V5 before extra roots. Alternative: only env vars (misses a Tools install that did not write the variable).

2. **Put the runtime folder first on PATH and preload the DLL** — the wrapper’s own static ctor already `SetDllImportResolver` and `TryLoad("Processing.NDI.Lib.x64.dll")` by file name. A second resolver throws `A resolver is already set for the assembly`. Alternative: our own resolver (shipped in 0.7.31, broken on Windows).

3. **Mac does not use the locator** — existing dylib next to the app stays.

## Risks / Trade-offs

- [NDI Tools not installed] → Same as today: unavailable, install Tools. We do not download a redist.
- [Wrapper already owns the resolver] → Never call `SetDllImportResolver` on that assembly. Prepend PATH and preload before `NDIlib.initialize()`.
