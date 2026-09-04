## Purpose

Keeps Scripture-tab verse cards in a three-column row that fills the pane width without stretching tall, and stops compact mid-typing from dumping a whole chapter onto the grid.

## ADDED Requirements

### Requirement: Scripture cards fill columns and hug height

When the Scripture tab shows verse cards, they SHALL lay out in three columns that together fill the list width. Each card’s height SHALL follow its preview plus label bar. The grid MUST NOT stretch a short result set to the pane height.

#### Scenario: One or two verses stay short

- **WHEN** the Scripture tab shows one or two verse cards
- **THEN** those cards are not as tall as the pane
- **AND** a card’s width is about one third of the list

#### Scenario: Resize keeps three columns

- **WHEN** the operator resizes the window
- **THEN** three cards still fit on one row
- **AND** card width grows or shrinks with the list so a large empty column does not open on the right

### Requirement: Compact typing without a colon is not a finished search

A typed query that glues a book to digits with no colon (`John3`) SHALL be treated as an unfinished reference. The Scripture tab MUST NOT load a chapter or phrase results for that string.

#### Scenario: John3 while typing John3:16

- **WHEN** the operator types `John3` in the Scripture tab search box
- **THEN** no verse cards are shown for that query
- **AND** John 3 is not loaded as a whole chapter

#### Scenario: Spaced chapter still works

- **WHEN** the operator types `John 3`
- **THEN** the Scripture tab shows John 3 as a chapter
