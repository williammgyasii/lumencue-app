## Context

See proposal.md. `ElevenLabsTranscriptionService.EngineName` is `ElevenLabs · Scribe v2`. Start status is `Connecting to ElevenLabs...`. Deepgram has the same pattern. `ai-listening-panel` does not mention vendor copy.

## Goals / Non-Goals

**Goals:** One Core copy source the services use for operator-visible strings.

**Non-Goals:** Changing logs, mint API, or hiding the engine chip.

## Decisions

1. **`SttOperatorCopy` in Core** — `EngineLabel`, `Connecting`. Alternative: string replace in each service (easy to miss Deepgram).

2. **Keep Listening / reconnect lines** that are already generic.

3. **Map Scribe errors to `Transcription error`** so API text cannot leak the product name.

## Risks / Trade-offs

- [Support cannot see which vendor from the desk] → Logs still name the vendor.
