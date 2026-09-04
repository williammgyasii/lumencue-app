## Purpose

Defines when live video loops and when it plays once, so event clips cannot restart themselves while motion backgrounds still cycle.

## ADDED Requirements

### Requirement: Media-tab video plays once

When the operator sends a Media-tab video live, the system SHALL play that clip from the start through to the end exactly once. The system MUST NOT restart the clip automatically when it reaches the end.

#### Scenario: Event clip finishes

- **WHEN** the operator sends a Media-tab video live and the clip reaches its last frame
- **THEN** playback does not restart from the beginning

#### Scenario: Sending the same clip again

- **WHEN** the operator sends the same Media-tab video live again after it has finished
- **THEN** the clip starts from the beginning and plays through once

### Requirement: Finished Media-tab video holds the last frame

When a Media-tab video reaches the end, the system SHALL keep the last decoded frame on the target until the operator clears that target or sends different media. The system MUST NOT blank the screen solely because the clip ended. Audio for that clip MUST stop at the end.

#### Scenario: Hold last frame after play-once

- **WHEN** a live Media-tab video reaches the end and the operator has not cleared it
- **THEN** the last frame remains visible on that target and its audio is silent

#### Scenario: Clear after play-once

- **WHEN** the operator clears a target whose Media-tab video has already finished
- **THEN** that media is removed from the target (same as clearing a still-playing clip)

### Requirement: Still images have no loop behavior

Still Media-tab images SHALL remain on the target until the operator clears them or sends different media. The system MUST NOT apply play-once or loop rules to images.

#### Scenario: Image stays until replaced

- **WHEN** the operator sends a still image live
- **THEN** the image stays on the target until the operator clears it or sends different media

### Requirement: Live backgrounds keep looping

Live background videos and background tile preview videos SHALL loop continuously for as long as that background remains selected.

#### Scenario: Motion bed keeps cycling

- **WHEN** the operator selects a video live background
- **THEN** the clip restarts from the beginning each time it reaches the end, until a different background is selected or the background is cleared

#### Scenario: Thumbnail preview still loops

- **WHEN** a video background tile is shown in the background picker
- **THEN** its muted preview clip loops
