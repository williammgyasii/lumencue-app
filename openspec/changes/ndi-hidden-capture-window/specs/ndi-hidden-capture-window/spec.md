## Purpose

Keeps the NDI program-feed capture window paintable at full canvas size without covering or blocking the operator desktop.

## ADDED Requirements

### Requirement: Capture window is invisible

The NDI capture window MUST have opacity 0 so the operator does not see a black 1920×1080 surface when Windows snaps the window on-screen.

#### Scenario: Opacity is zero

- **WHEN** NDI capture chrome is applied
- **THEN** window opacity is 0

### Requirement: Capture window does not steal focus

Starting NDI MUST NOT activate the capture window or put it on the taskbar.

#### Scenario: No activate and no taskbar

- **WHEN** NDI capture chrome is applied
- **THEN** the window does not show activated
- **AND** the window is not on the taskbar

### Requirement: Capture window does not eat clicks

If the capture window is on-screen, it MUST NOT receive pointer hits so the operator can keep using the desktop under it.

#### Scenario: Hits pass through

- **WHEN** NDI capture chrome is applied
- **THEN** the window is not hit-test visible

### Requirement: Capture stays full canvas and not minimized

The capture window MUST stay the program canvas size and MUST NOT be minimized. Shrinking or minimizing MUST NOT be used as the hide strategy.

#### Scenario: Full size, normal state

- **WHEN** NDI capture chrome is applied
- **THEN** width is the program canvas width
- **AND** height is the program canvas height
- **AND** the window is not minimized
