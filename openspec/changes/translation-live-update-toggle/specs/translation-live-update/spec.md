## Purpose

Lets the operator choose whether picking a new Bible translation rewrites scripture that is already live, without a confirmation dialog.

## ADDED Requirements

### Requirement: Live update is optional and defaults on

The system SHALL persist a boolean preference that controls whether a translation change re-projects live scripture. When the preference has never been set, the system SHALL treat it as enabled so existing operator behavior is unchanged.

#### Scenario: Fresh install keeps today's behavior

- **WHEN** the operator has never saved the preference and scripture is live
- **AND** they change the selected translation
- **THEN** the live verse is re-rendered in the new translation

### Requirement: Disabled preference leaves live scripture alone

When the preference is disabled and the operator changes the selected translation, the system SHALL update search, library, and picker to the new translation, and MUST NOT re-project the verse that is already live.

#### Scenario: Change translation while live with preference off

- **WHEN** scripture is live, the preference is disabled, and the operator selects a different translation
- **THEN** the projector keeps the verse in the translation it was sent in
- **AND** subsequent searches use the newly selected translation

### Requirement: Sending a verse always uses the selected translation

Sending a verse live (click / double-click / compare card) SHALL use the currently selected translation regardless of the preference. The preference only gates the automatic live refresh that follows a translation picker change.

#### Scenario: Send after changing translation with preference off

- **WHEN** the preference is disabled, the operator changes translation, then sends a verse live
- **THEN** that verse appears in the newly selected translation

### Requirement: Setting lives with other Behavior defaults

The preference SHALL be editable in Settings → Behavior next to the existing operator workflow toggles, and SHALL persist across launches.

#### Scenario: Preference survives restart

- **WHEN** the operator turns the toggle off and restarts the app
- **THEN** the preference is still off
