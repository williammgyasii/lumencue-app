## Purpose

Stops NDI from blocking the operator UI thread while waiting for its own video clock.

## ADDED Requirements

### Requirement: Send does not clock video

NDI capture MUST create the sender without video clocking. The capture timer already spaces frames.

#### Scenario: Clock video is off

- **WHEN** NDI capture chrome is applied
- **THEN** video clocking is off
