## Why

Windows booths install NDI Tools for OBS, but LumenCue still fails with `Unable to load DLL 'NDILib'`. The wrapper looks for `NDILib.dll`; Tools installs `Processing.NDI.Lib.x64.dll` and sets `NDI_RUNTIME_DIR_V6`. Operators cannot copy DLLs into the Velopack folder — the next update wipes it.

## What Changes

Explored: **A** load the installed NDI Runtime/Tools library (chosen), **B** bundle the DLL in our installer, **C** per-PC copy into `LumenCue\current`. **A** is what NDI documents (`NDI_RUNTIME_DIR_V6`) and what OBS already does.

- On Windows, before NDI starts, resolve the installed `Processing.NDI.Lib.x64.dll`
- Map the `NDILib` P/Invoke to that file
- Prefer V6 over V5; do not invent a path when the file is missing
- Mac stays as-is (bundled dylib)

**Not in this change:** bundling the NDI binary, OBS/DistroAV install, renaming the source, UI copy.

## Capabilities

### New Capabilities

- `windows-ndi-runtime`: How Windows finds the NDI native library churches already installed.

### Modified Capabilities

- None. No archived NDI spec.

## Impact

- `NdiRuntimeLocator` in Core
- `NdiOutputService` registers a Windows DllImport resolver before `NDIlib.initialize()`
