## Purpose

Offers only translations the booth can download once from helloao or our hosted JSON, after the API.Bible plan is gone.

## ADDED Requirements

### Requirement: Picker lists helloao and hosted codes only

The operator translation picker MUST offer exactly the helloao booth set `KJV`, `BSB`, `WEB`, `NET`, `ASV`, `LSV`, `YLT` plus the hosted set `ESV`, `GNT`, `TPT`, `TLB`, `AMPC`. It MUST NOT offer `NIV`, `NKJV`, `NLT`, `MSG`, `AMP`, or `CSB`.

#### Scenario: Offered codes exclude API.Bible-only translations

- **WHEN** the picker translation list is built
- **THEN** it contains `KJV`, `BSB`, `WEB`, `NET`, `ASV`, `LSV`, `YLT`, `ESV`, `GNT`, `TPT`, `TLB`, and `AMPC`
- **AND** it does not contain `NIV`, `NKJV`, `NLT`, `MSG`, `AMP`, or `CSB`

### Requirement: KJV and NET resolve to helloao download ids

`KJV` MUST map to helloao id `eng_kjv` and `NET` MUST map to `eng_net` even when the helloao catalog short names are `KJAV` and `NETB`. Those two codes MUST be treated as bulk-cacheable.

#### Scenario: Catalog short names do not hide KJV or NET

- **WHEN** the helloao catalog maps `KJAV` to `eng_kjv` and `NETB` to `eng_net`
- **AND** no catalog row uses the short name `KJV` or `NET`
- **THEN** resolving `KJV` returns `eng_kjv`
- **AND** resolving `NET` returns `eng_net`
- **AND** both codes are bulk-cacheable

### Requirement: Seed picker has no paid codes

The in-memory picker seed shown before the catalog load finishes MUST NOT include `NIV`, `NKJV`, `NLT`, `MSG`, `AMP`, or `CSB`.

#### Scenario: First paint seed matches the booth set

- **WHEN** the scripture search view model is constructed
- **THEN** its translation seed includes `KJV` and `BSB`
- **AND** it does not include `NIV` or `MSG`
