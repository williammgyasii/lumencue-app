## Purpose

Stops NDI from painting and sending a full program frame when the slide has not changed, so a booth PC running OBS on the same machine is not software-rendering 1080p the whole service.

## ADDED Requirements

### Requirement: Unchanged static frames are not painted

When skip-unchanged is on, a capture tick MUST NOT paint if program content is unchanged and no motion background is live.

#### Scenario: Skip static unchanged

- **WHEN** skip-unchanged is on and content is unchanged and motion is not live
- **THEN** the capture tick must not paint

### Requirement: Changed or motion frames are painted

A capture tick MUST paint when program content changed or a motion background is live.

#### Scenario: Paint when content changed

- **WHEN** skip-unchanged is on and content changed
- **THEN** the capture tick must paint

#### Scenario: Paint when motion is live

- **WHEN** skip-unchanged is on and motion is live
- **THEN** the capture tick must paint even if the generation is unchanged

### Requirement: Skip-unchanged is on

NDI capture MUST skip unchanged static frames.

#### Scenario: Skip flag is on

- **WHEN** NDI capture chrome is applied
- **THEN** skip-unchanged is on
