## Purpose

When the operator saves a change to the song that is on the projector, the live slide updates to the new lyrics. Edits to a song that is not live only refresh the operator cards.

## ADDED Requirements

### Requirement: Saving the live song rewrites that slide on the projector

If a lyric slide from song S is live, and the operator saves an edit to song S, the system SHALL re-project the same slide with the saved text. The operator MUST NOT need to press Space or Enter.

#### Scenario: Typo fix while the chorus is live

- **WHEN** chorus of song S is live
- **AND** the operator edits that chorus and saves
- **THEN** the projector shows the corrected chorus
- **AND** the Now Singing card for that chorus still shows the LIVE ring

### Requirement: Saving a song that is not live leaves the projector alone

If nothing from song S is live, saving S SHALL update the library and Now Singing cards and MUST NOT change the projector.

#### Scenario: Edit a song while scripture is live

- **WHEN** a Bible verse is live
- **AND** the operator saves an edit to a song
- **THEN** the projector still shows that verse

#### Scenario: Edit song A while song B is live

- **WHEN** a slide from song B is live
- **AND** the operator saves an edit to song A
- **THEN** the projector still shows song B

### Requirement: Deleted live slide is not replaced automatically

If the operator deletes or splits away the slide that was live, the system MUST NOT send a different slide. The projector keeps what it has until the operator sends a card.

#### Scenario: Live section removed in the editor

- **WHEN** verse 2 of song S is live
- **AND** the operator deletes verse 2 and saves
- **THEN** the projector still shows the old verse 2
- **AND** no Now Singing card has the LIVE ring

### Requirement: Full editor and quick-edit share the rule

Quick-edit on a card, add-slide, and Save in the full song editor SHALL all use the same live-sync rule.

#### Scenario: Full editor save while that song is live

- **WHEN** a slide from song S is live
- **AND** the operator saves song S from the full editor
- **THEN** the projector shows the updated text for that same slide
