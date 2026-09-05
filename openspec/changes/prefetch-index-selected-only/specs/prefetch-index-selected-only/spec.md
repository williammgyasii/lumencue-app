## Purpose

Lets picker Bibles download in the background without building a topical search index for every translation.

## ADDED Requirements

### Requirement: Only the selected translation is indexed after cache

After a translation finishes caching, the topical search index MUST start only when that translation is the currently selected one.

#### Scenario: Follow-on picker code is not indexed

- **WHEN** the selected translation is `NIV`
- **AND** a prefetch cache completes for `NKJV`
- **THEN** topical indexing is not started for `NKJV`

#### Scenario: Selected translation is indexed

- **WHEN** the selected translation is `NIV`
- **AND** a cache completes for `NIV`
- **THEN** topical indexing is started for `NIV`
