## Purpose

Lets the operator pick up to two Compare translations and see exactly those cards, minus the one already live.

## ADDED Requirements

### Requirement: Picks are the source of truth

The Compare pane SHALL show one card per chosen translation, up to two. Choosing one SHALL show one card. Choosing two SHALL show two. Choosing none SHALL show no cards. The system MUST NOT fill an empty slot from the full translation list.

#### Scenario: One pick shows one card

- **WHEN** the operator has checked only `NIV` in Compare
- **AND** the live translation is not `NIV`
- **THEN** Compare shows one card
- **AND** that card is `NIV`

#### Scenario: Two picks show two cards

- **WHEN** the operator has checked `NIV` and `KJV`
- **AND** neither is the live translation
- **THEN** Compare shows two cards
- **AND** those cards are `NIV` and `KJV`

#### Scenario: Unchecking does not refill

- **WHEN** two translations are chosen and the operator unchecks one
- **THEN** Compare shows the remaining chosen translation only
- **AND** a different translation is not auto-selected

### Requirement: Active translation is not a compare card

A chosen translation that is the one currently selected / live MUST NOT appear as a Compare card. Other chosen translations still appear.

#### Scenario: Live version is skipped

- **WHEN** the operator has chosen `BSB` and `NIV`
- **AND** the live translation is `BSB`
- **THEN** Compare shows `NIV` only

### Requirement: Cap stays two

The operator MUST NOT have more than two translations checked at once. Unchecked rows MAY lock while two are on; checked rows MUST stay toggleable so one can be cleared.

#### Scenario: Third check is rejected

- **WHEN** two translations are already chosen
- **AND** the operator tries to check a third
- **THEN** the third stays unchecked
- **AND** the first two remain chosen
