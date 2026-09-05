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
