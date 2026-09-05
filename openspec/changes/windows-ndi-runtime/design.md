## Context

See proposal.md. `NDILibDotNetCoreBase` P/Invokes `NDILib`. NDI Tools/Runtime on Windows ships `Processing.NDI.Lib.x64.dll` and sets `NDI_RUNTIME_DIR_V6` (or V5). The Windows publish does not copy that DLL. Probe fails with `DllNotFoundException`; the Screens toggle can still look on.

## Goals / Non-Goals

**Goals:** Find the installed Windows runtime and load it under the `NDILib` name before initialize.

**Non-Goals:** Shipping the NDI binary, changing Mac load, changing the OBS source name.

## Decisions

1. **`NdiRuntimeLocator` in Core** — given env dirs and extra roots, return the first existing `Processing.NDI.Lib.x64.dll`. V6 before V5 before extra roots. Alternative: only env vars (misses a Tools install that did not write the variable).

2. **`NativeLibrary.SetDllImportResolver` on the NDI wrapper assembly** — map `NDILib` to that full path. Alternative: copy/rename into the app folder at runtime (Velopack current is replaceable; writing there is fragile).

3. **Mac does not use the locator** — existing dylib next to the app stays.

## Risks / Trade-offs

- [NDI Tools not installed] → Same as today: unavailable, install Tools. We do not download a redist.
- [Resolver registered too late] → Register once, before the first `NDIlib.initialize()`.
