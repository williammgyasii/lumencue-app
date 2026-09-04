## Purpose

Gives the operator a fixed keyboard map on the operator window so they can change mode, toggle Output, open Settings, and see the keys — without hijacking typing or sending live by accident.

## ADDED Requirements

### Requirement: Mode keys switch workspace and do not go live

When the operator window is focused and the key source is not a text box, the system SHALL treat `1` as Bible, `2` as Songs, `3` as Media, and `4` as Notes. Top-row and numpad digits SHALL mean the same thing. Switching mode this way MUST NOT send the current preview live. If that mode is already selected, the workspace SHALL stay there and still MUST NOT send live.

#### Scenario: Digit switches mode

- **WHEN** the operator window is focused, no text box has focus, and the operator presses `2`
- **THEN** the workspace is Songs

#### Scenario: Mode key does not send live

- **WHEN** a verse is in preview and the operator presses `1`
- **THEN** the workspace is Bible
- **AND** the projector is unchanged

#### Scenario: Numpad matches the top row

- **WHEN** the operator presses numpad `3` with no text box focused
- **THEN** the workspace is Media

### Requirement: Output and Settings have single keys

When the operator window is focused and the key source is not a text box, the system SHALL toggle screen Output on `O` and SHALL open Settings on `,`.

#### Scenario: O toggles Output

- **WHEN** Output is on and the operator presses `O` with no text box focused
- **THEN** Output is off

#### Scenario: Comma opens Settings

- **WHEN** the operator presses `,` with no text box focused
- **THEN** the Settings dialog is shown

### Requirement: Question mark toggles a cheatsheet

When the operator window is focused and the key source is not a text box, the system SHALL toggle an on-window cheatsheet when the operator presses `?`. The overlay is session-only and MUST NOT persist across restart. The cheatsheet SHALL list the live keys (arrows, Space/Enter, Esc) and the new keys (`1`–`4`, `O`, `,`, `?`).

#### Scenario: First press shows the map

- **WHEN** the cheatsheet is hidden and the operator presses `?`
- **THEN** the overlay is visible
- **AND** it lists Bible, Songs, Media, Notes, Output, Settings, send live, blank, and page live

#### Scenario: Second press hides the map

- **WHEN** the cheatsheet is visible and the operator presses `?`
- **THEN** the overlay is hidden

### Requirement: Typing and modifiers are never stolen

The system MUST NOT run the shortcut map when the key source is a text box. Digit, letter, and comma actions MUST NOT run when Ctrl, Cmd, Alt, or Shift is held. `?` is the exception: it MAY use Shift because that is how a question mark is typed on a US keyboard.

#### Scenario: Digit in a search box stays text

- **WHEN** the search box is focused and the operator presses `1`
- **THEN** the workspace mode does not change
- **AND** the character is typed as usual

#### Scenario: Shifted digit does not change mode

- **WHEN** no text box is focused and the operator presses Shift+`1`
- **THEN** the workspace mode does not change

### Requirement: Existing live keys stay

Left/Right SHALL still page live. Up/Down SHALL still step live only in Now Singing or an open note. Space and Enter SHALL still send preview live. Esc SHALL blank the projector, except: when a text box is focused it SHALL clear that box; when the cheatsheet is visible it SHALL hide the cheatsheet and MUST NOT blank.

#### Scenario: Space still sends live

- **WHEN** no text box is focused and the operator presses Space
- **THEN** the current preview is sent live

#### Scenario: Esc hides the cheatsheet before blanking

- **WHEN** the cheatsheet is visible and the operator presses Esc
- **THEN** the overlay is hidden
- **AND** the projector is not blanked
