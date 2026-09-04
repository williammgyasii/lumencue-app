## Purpose

Keeps each operator workspace (Bible, Songs, Media) where the operator left it, and never sends a verse or song live just because a tab switched or a list rebuilt.

## ADDED Requirements

### Requirement: Programmatic selection never goes live

When a list item is selected because the operator switched workspace mode, the list rebuilt, or a saved place was restored, the system MUST NOT send that item live. This MUST hold even when single-click-goes-live is enabled. The system MAY update the preview to that item.

#### Scenario: Mode restore while single-click-goes-live is on

- **WHEN** single-click-goes-live is enabled
- **AND** a verse or song is selected because the operator switched workspace mode
- **THEN** that item is not sent live

#### Scenario: List rebuild while single-click-goes-live is on

- **WHEN** single-click-goes-live is enabled
- **AND** the content list rebuilds and the first row becomes selected
- **THEN** that row is not sent live

### Requirement: Operator clicks still follow the click policy

An operator click or double-click on a verse, song, or media tile SHALL still follow the existing live-click policy. This change only gates selections that the workspace applied itself.

#### Scenario: Explicit double-click still goes live

- **WHEN** the operator double-clicks a verse or song
- **THEN** that item is sent live

#### Scenario: Single-click-goes-live still honors an operator click

- **WHEN** single-click-goes-live is enabled
- **AND** the operator clicks a verse or song (not a mode switch)
- **THEN** that item is sent live

### Requirement: Each mode restores its last session place

Switching Bible, Songs, or Media SHALL restore that mode’s last place from the current session. Bible SHALL restore the last search or chapter, not reset to Genesis 1. Songs SHALL restore the last search or open song, not wipe to the full library. Media SHALL keep the last folder. A mode the operator has not visited yet in this session MAY use today’s first-visit default.

#### Scenario: Return to Bible keeps the last chapter

- **WHEN** the operator is browsing a chapter other than Genesis 1 in Bible
- **AND** they switch to Songs, then back to Bible
- **THEN** that same chapter is showing, not Genesis 1

#### Scenario: Return to Songs keeps the last search

- **WHEN** the operator has a song search (or an open song) in Songs
- **AND** they switch to Bible, then back to Songs
- **THEN** that same search or song is showing, not a wiped full library

#### Scenario: Return to Media keeps the last folder

- **WHEN** the operator has a Media folder selected
- **AND** they switch to Bible, then back to Media
- **THEN** that same folder is still selected

#### Scenario: First visit to Bible can still open Genesis 1

- **WHEN** the operator has not opened Bible yet in this session
- **THEN** Bible may open on Genesis 1
