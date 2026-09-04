## Purpose

Orders Find Scripture topical and phrase results so exact wording beats a loosely related semantic hit.

## ADDED Requirements

### Requirement: Famous phrase ranks the matching verse first

When the operator describes a well-known verse in its own words, and that verse’s text contains the query as a phrase, that verse MUST be first in the result list.

#### Scenario: God so loved the world

- **WHEN** Find Scripture is queried with `god so loved the world`
- **AND** John 3:16 is among the candidates and its text contains that phrase
- **THEN** the first result is John 3:16

### Requirement: Phrase and keyword hits outrank semantic-only

A verse that matches the query as a contiguous phrase or by word-boundary keywords MUST rank above every verse that matched only by embedding similarity.

#### Scenario: High cosine cannot beat a phrase hit

- **WHEN** one candidate contains the query phrase
- **AND** another candidate has a semantic-only cosine of 0.90 and no phrase or keyword match
- **THEN** the phrase hit is ordered before the semantic-only hit

#### Scenario: Substring inside a longer word is not a keyword match

- **WHEN** the query token is `god`
- **AND** a verse contains `godly` but not the word `god`
- **THEN** that verse is not treated as a keyword match

### Requirement: Weak semantic hits are dropped

A semantic-only hit whose cosine is below 0.40 MUST NOT appear in the results.

#### Scenario: Loose cosine is omitted

- **WHEN** a verse matches only by embedding with cosine 0.30
- **AND** it has no phrase or keyword match
- **THEN** it is not in the result list

### Requirement: Other search paths stay as they are

Typed Scripture-tab reference parsing and spoken `TryParse` MUST NOT gain new topical ranking rules. Find Scripture MUST still be topical/hybrid search, not a typed-reference expander.

#### Scenario: Typed compact refs are unchanged

- **WHEN** the operator types `John3:16` in the Scripture tab
- **THEN** it still parses as John 3:16 via the typed reference path
- **AND** Find Scripture ranking is not applied to that lookup
