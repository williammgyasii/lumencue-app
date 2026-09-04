## Why

AI Listening shows “Connecting to ElevenLabs…” and “ElevenLabs · Scribe v2” on the operator desk. That names the vendor in front of anyone looking at the booth. The desk should only say it is talking to a transcription agent.

## What Changes

Explored: **A** generic operator copy (`Transcription agent` / `Connecting to transcription agent…`) while logs stay vendor-specific (chosen), **B** hide the engine chip entirely, **C** rename only the connecting line. **A** keeps a status the operator can trust without naming the vendor.

- Engine chip MUST NOT contain ElevenLabs, Scribe, or Deepgram
- Connecting / error status MUST NOT name those vendors
- Server logs MAY still name the vendor

**Not in this change:** token minting, reconnect policy, lower-third layout.

## Capabilities

### New Capabilities

- `stt-operator-copy`: What the operator sees while speech-to-text connects and runs.

### Modified Capabilities

- None. `ai-listening-panel` covers clip/scroll, not vendor names.

## Impact

- `SttOperatorCopy` in Core
- `ElevenLabsTranscriptionService` / `DeepgramTranscriptionService` operator strings
