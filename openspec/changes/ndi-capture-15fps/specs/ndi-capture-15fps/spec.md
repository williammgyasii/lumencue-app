## Purpose

Caps NDI program-feed capture at a lyric-safe frame rate so a booth PC is not asked to software-render 1080p30 all service.

## ADDED Requirements

### Requirement: Capture runs at 15 frames per second

The NDI capture clock MUST be 15 frames per second, not 30.

#### Scenario: Fifteen fps

- **WHEN** NDI capture chrome is applied
- **THEN** frames per second is 15
- **AND** frames per second is less than 30
