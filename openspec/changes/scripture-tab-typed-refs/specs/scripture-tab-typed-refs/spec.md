## Purpose

Makes the Bible Scripture-tab search box treat compact, numbered-book, comma, and cross-chapter typed references as full passage lookups instead of phrase search.

## ADDED Requirements

### Requirement: Compact book-and-chapter is a reference

A typed query that is a book name or alias immediately followed by a chapter and verse, with no space, SHALL parse as that reference. The Scripture tab MUST return that verse, not a phrase ranking.

#### Scenario: No space after the book

- **WHEN** the operator types `John3:16` in the Scripture tab search box
- **THEN** the results are John 3:16
- **AND** the query is not treated as a phrase search

### Requirement: Numbered-book prefixes resolve to 1/2/3 John and kin

Typed `I` / `II` / `III` and `1st` / `2nd` / `3rd` before a book SHALL mean books 1, 2, and 3. `I John 3:16` and `1st John 3:16` SHALL return 1 John 3:16.

#### Scenario: Roman and ordinal 1 John

- **WHEN** the operator types `I John 3:16` or `1st John 3:16`
- **THEN** the results are 1 John 3:16

### Requirement: Comma lists return every listed verse

A same-chapter list of verses separated by commas SHALL return each listed verse. It MUST NOT return only the first.

#### Scenario: Two verses in one chapter

- **WHEN** the operator types `John 3:16,18`
- **THEN** the results include John 3:16
- **AND** the results include John 3:18

### Requirement: Cross-chapter ranges return the full span

A range whose end chapter differs from the start chapter SHALL return every verse from the start verse through the end of the start chapter, then verse 1 through the end verse of the end chapter.

#### Scenario: John 3 into John 4

- **WHEN** the operator types `John 3:16-4:2`
- **THEN** the results include John 3:16
- **AND** the results include John 4:2
- **AND** verses of John 3 after 16 that exist in that chapter are included

### Requirement: Spoken and Find Scripture stay as they are

The live spoken/AI parser MUST remain strict (no compact / roman / comma / cross-chapter expansion on free speech). The Find Scripture tab MUST keep using topical/hybrid search only.

#### Scenario: Spoken path does not gain typed shortcuts

- **WHEN** speech contains a loose fragment that is not a strict reference
- **THEN** it is not auto-corrected by the new typed rules
