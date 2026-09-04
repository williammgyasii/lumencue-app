## Purpose

Makes the operator workspace readable at a glance: Material icons on the top tabs, a full-box active highlight, Bookmarks and Compare as two separate left views, and a tinted AI Listening panel under Program.

## ADDED Requirements

### Requirement: Top workspace tabs use Material icons

Bible, Songs, Media, Notes, and Themes SHALL each show a Material icon kind next to the label. The system MUST NOT use emoji or dingbat glyphs (cross, beamed notes, play triangle, memo) as those tab icons.

#### Scenario: Every top tab has a Material icon kind and a label

- **WHEN** the operator looks at the top workspace tabs
- **THEN** each of Bible, Songs, Media, Notes, and Themes has a non-empty label and a Material icon kind
- **AND** none of those kinds is an emoji or unicode dingbat

### Requirement: Active tab is a highlighted box; others are grayed

The active workspace mode (Bible, Songs, Media, or Notes) SHALL highlight the entire tab button (fill and border). Inactive workspace tabs SHALL be grayed (lower opacity). Themes is a dialog launcher and MUST NOT take the workspace-active highlight.

#### Scenario: Active tab is fully highlighted

- **WHEN** Bible mode is active
- **THEN** the Bible tab is drawn as a highlighted box
- **AND** Songs, Media, and Notes are grayed

#### Scenario: Themes is never the workspace-active tab

- **WHEN** any workspace mode is active
- **THEN** Themes is not marked as the active workspace tab

### Requirement: Compare is a separate left view from Bookmarks

Compare Scriptures SHALL appear in the left sidebar as its own view, sibling to Bookmarks — not nested inside the Bookmarks panel. It SHALL remain Bible-only. It MUST NOT occupy the right rail under Program.

#### Scenario: Compare is a separate view in Bible mode

- **WHEN** Bible mode is active
- **THEN** Compare Scriptures is in the left column as its own view
- **AND** it is not nested inside Bookmarks
- **AND** Compare is not under Program

#### Scenario: Compare stays hidden outside Bible

- **WHEN** Songs, Media, or Notes is active
- **THEN** Compare Scriptures is not shown

#### Scenario: Compare cards stay inside the pane

- **WHEN** Bible mode is active and compare cards are showing
- **THEN** card padding plus borders fit inside the 248px left sidebar
- **AND** the pane clips so cards do not paint outside its box

### Requirement: AI Listening sits under Program on the right

AI Listening SHALL appear in the right rail under Program. The full-width bottom AI Listening bar MUST NOT remain. Its panel background SHALL be a muted blue tint that is distinct from the Program rail and from the violet compare accent.

#### Scenario: AI Listening is under Program

- **WHEN** the operator window is showing the right rail
- **THEN** AI Listening is under Program
- **AND** there is no bottom-row AI Listening bar

#### Scenario: AI Listening background is tinted

- **WHEN** AI Listening is shown under Program
- **THEN** its panel background is not the same as the Program rail fill
- **AND** the tint stays in the existing blue family (not violet)
