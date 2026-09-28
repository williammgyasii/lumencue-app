## Purpose

Puts translations that are already finished in the booth SQLite cache back on the operator picker after the downloadable set no longer includes them.

## ADDED Requirements

### Requirement: Complete local caches appear on the picker

When the picker list is built, it MUST include every helloao and hosted code, and MUST also include each translation whose local cache is marked complete. Codes that are not offered and not complete MUST NOT appear.

#### Scenario: Cached NIV NLT AMP join the picker

- **WHEN** the downloadable picker is the helloao and hosted set
- **AND** the local cache is complete for `NIV`, `NLT`, and `AMP`
- **THEN** the picker contains `NIV`, `NLT`, and `AMP`
- **AND** it still contains `KJV` and `BSB`

#### Scenario: Nothing extra when the cache is empty of paid codes

- **WHEN** the downloadable picker is the helloao and hosted set
- **AND** no complete local cache exists outside that set
- **THEN** the picker does not contain `NIV`, `NLT`, or `AMP`
