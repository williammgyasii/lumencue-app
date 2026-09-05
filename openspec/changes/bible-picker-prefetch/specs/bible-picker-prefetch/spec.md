## Purpose

Prefetches every Bible on the operator picker in the background so switching translations is not an empty cache.

## ADDED Requirements

### Requirement: Selected translation is first

The download queue MUST start with the currently selected translation when that code is non-empty.

#### Scenario: Selected leads the queue

- **WHEN** the selected translation is `NIV` and the picker includes `KJV` and `NIV`
- **THEN** the queue starts with `NIV`

### Requirement: Every picker code is queued

Every translation offered in the picker MUST appear in the queue. Codes already listed as selected MUST NOT be repeated.

#### Scenario: Picker codes follow selected

- **WHEN** the selected translation is `NIV`
- **AND** the picker is `KJV`, `NIV`, `ESV`
- **THEN** the queue is `NIV`, `KJV`, `ESV`

### Requirement: Helloao extras stay out

English translations that are not in the picker MUST NOT be added to the queue.

#### Scenario: Non-picker English is omitted

- **WHEN** the picker is `KJV` and `NIV`
- **AND** helloao also lists `WEB`
- **THEN** the queue does not contain `WEB`
