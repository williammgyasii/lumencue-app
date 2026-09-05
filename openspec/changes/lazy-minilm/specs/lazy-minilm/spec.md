## Purpose

Keeps MiniLM and the topical Bible index off the heap until the operator actually searches, so a projection-only booth does not sit at 1 GB.

## ADDED Requirements

### Requirement: Embeddings do not load at launch

The semantic embedding engine MUST NOT be marked to load during app startup.

#### Scenario: Startup does not load embeddings

- **WHEN** embedding policy is applied
- **THEN** embeddings are not loaded at launch

### Requirement: Cache does not start the topical index

Finishing a Bible download MUST NOT start topical indexing.

#### Scenario: Index after cache is off

- **WHEN** embedding policy is applied
- **THEN** topical indexing after a Bible cache is off
