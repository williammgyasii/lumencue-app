## Purpose

Windows LumenCue loads the NDI library churches already installed with NDI Tools or NDI Runtime, so the program feed can announce without a manual DLL copy.

## ADDED Requirements

### Requirement: Installed V6 runtime wins

When `NDI_RUNTIME_DIR_V6` points at a folder that contains `Processing.NDI.Lib.x64.dll`, the locator MUST return that file.

#### Scenario: V6 env path is used

- **WHEN** the V6 runtime directory contains `Processing.NDI.Lib.x64.dll`
- **THEN** the resolved library path is that file

### Requirement: V5 is the fallback

When V6 is missing and `NDI_RUNTIME_DIR_V5` contains the library, the locator MUST return the V5 file.

#### Scenario: V5 env path is used

- **WHEN** the V6 runtime directory is empty or missing the library
- **AND** the V5 runtime directory contains `Processing.NDI.Lib.x64.dll`
- **THEN** the resolved library path is the V5 file

### Requirement: Missing file is not a path

The locator MUST NOT return a path when the library file does not exist.

#### Scenario: No runtime on disk

- **WHEN** no candidate directory contains `Processing.NDI.Lib.x64.dll`
- **THEN** the resolved library path is empty

### Requirement: Runtime folder is first on PATH

When a Windows library path is known, its directory MUST be first on the process PATH so the NDI wrapper can `TryLoad` `Processing.NDI.Lib.x64.dll` by file name.

#### Scenario: Library directory leads PATH

- **WHEN** the locator has resolved a Windows library file
- **THEN** that file’s directory is the first entry on PATH
