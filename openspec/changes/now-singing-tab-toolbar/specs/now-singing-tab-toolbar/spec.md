## Purpose

Keeps Songs-mode chrome on one row so opening a song does not shift the slide grid, and shows the loaded song as a distinct colored bubble on the tab strip.

## ADDED Requirements

### Requirement: Now Singing chrome shares the tab row

When a song is open on the Now Singing tab, the song title and the Follow, Lines, add-slide, and size controls SHALL appear on the same row as the Now Singing and Songs tabs, aligned to the far right. The operator MUST NOT see a second title bar under those tabs.

#### Scenario: Open song does not add a second chrome row

- **WHEN** a song is open on the Now Singing tab
- **THEN** title and song controls sit on the tab row
- **AND** there is no inner title bar above the slides

### Requirement: Song title is a distinct bubble

The loaded song title SHALL render as a pill/bubble whose fill and text colors differ from the tab labels and from the Follow / Lines / size controls.

#### Scenario: Title reads as its own chip

- **WHEN** a song is open on the Now Singing tab
- **THEN** the title is in a colored bubble
- **AND** that bubble color is not the same as the tab text or the control chrome

### Requirement: Toolbar is only on Now Singing with a song

The title bubble and song controls MUST NOT appear on the Songs search tab. They MUST NOT appear when no song is loaded.

#### Scenario: Songs tab and empty Now Singing hide the cluster

- **WHEN** the operator is on the Songs search tab, or Now Singing has no song
- **THEN** the title bubble and song controls are hidden
