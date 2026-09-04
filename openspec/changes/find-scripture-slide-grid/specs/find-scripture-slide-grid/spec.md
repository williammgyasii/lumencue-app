## Purpose

Keeps Find Scripture result cards in the same three-column, hug-height grid as the Scripture tab.

## ADDED Requirements

### Requirement: Find Scripture cards fill columns and hug height

When Find Scripture shows topical result cards, they SHALL lay out in three columns that together fill the list width. Each card’s height SHALL follow its preview plus caption. The grid MUST NOT stretch a short result set to the pane height.

#### Scenario: One or two hits stay short

- **WHEN** Find Scripture shows one or two result cards
- **THEN** those cards are not as tall as the pane
- **AND** a card’s width is about one third of the Find Scripture list

#### Scenario: Resize keeps three columns

- **WHEN** the operator resizes the window while Find Scripture results are showing
- **THEN** three cards still fit on one row
- **AND** card width grows or shrinks with the list so a large empty column does not open on the right
