## Purpose

Keeps AI Listening inside the right rail, scrolls the live transcript as speech arrives, shows only scriptures on the suggestions tab when the mic is off, and hides the Paraphrases subtab until that feature is rebuilt.

## ADDED Requirements

### Requirement: AI Listening stays inside the right rail

When AI Listening is expanded under Program, the panel SHALL clip to the remaining right-rail height and SHALL scroll its controls. It MUST NOT paint outside the rail.

#### Scenario: Expanded panel does not overflow the rail

- **WHEN** AI Listening is expanded under Program
- **THEN** the panel is clipped to the rail
- **AND** its body scrolls if the controls do not fit

### Requirement: Transcript is a mini-scroll that follows the latest words

The live transcript box SHALL have a fixed height, SHALL scroll inside that box, and SHALL keep the newest words in view as text is appended.

#### Scenario: New speech stays visible at the bottom

- **WHEN** the operator is listening and the transcript grows past the box
- **THEN** the box scrolls
- **AND** the newest words remain visible

### Requirement: Suggestions tab does not start listening

When the mic is off, the AI Suggestions tab MUST NOT show a Start Listening button or an “AI Listening is off” prompt. It SHALL show the scripture suggestion list. Start and Stop remain on the right-rail AI Listening panel.

#### Scenario: Off state shows scriptures only

- **WHEN** the mic is off and the operator opens AI Suggestions
- **THEN** Start Listening is not shown on that tab
- **AND** the scripture list is shown

### Requirement: Paraphrases subtab is hidden

The Paraphrases content tab MUST NOT appear in Bible mode. Its content pane MUST NOT be shown. The operator SHALL still have Scripture, AI Suggestions, and Find Scripture.

#### Scenario: Bible tabs omit Paraphrases

- **WHEN** Bible mode is active
- **THEN** there is no Paraphrases subtab
- **AND** Scripture, AI Suggestions, and Find Scripture remain
