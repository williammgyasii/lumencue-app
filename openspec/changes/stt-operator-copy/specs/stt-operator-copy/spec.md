## Purpose

Keeps vendor speech-to-text names off the operator desk so the booth only sees a transcription agent.

## ADDED Requirements

### Requirement: Engine label is generic

While listening, the engine name shown on the desk MUST be a generic agent label. It MUST NOT contain `ElevenLabs`, `Scribe`, or `Deepgram`.

#### Scenario: Engine chip has no vendor name

- **WHEN** the operator is listening
- **THEN** the engine name is `Transcription agent`
- **AND** it does not contain `ElevenLabs`

### Requirement: Connecting status is generic

The connecting status line MUST say the desk is connecting to the transcription agent. It MUST NOT name ElevenLabs or Deepgram.

#### Scenario: Connecting copy

- **WHEN** speech-to-text is connecting
- **THEN** the status is `Connecting to transcription agent...`
- **AND** it does not contain `ElevenLabs`
