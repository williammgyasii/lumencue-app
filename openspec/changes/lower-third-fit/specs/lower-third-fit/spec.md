## Purpose

Keeps live text inside the lower-third band by paging overflow and clipping the body box.

## ADDED Requirements

### Requirement: Text that fits stays one slide

When the body text fits the Lower Third body region at the theme’s pagination size, the deck MUST be a single slide.

#### Scenario: Short verse is one page

- **WHEN** a Lower Third theme projects a short verse that fits the body box
- **THEN** the deck has one slide

### Requirement: Overflow becomes another slide

When the body text is taller than the usable Lower Third body box, the deck MUST contain more than one slide. No page’s measured text MAY exceed that box.

#### Scenario: Long verse pages

- **WHEN** a Lower Third theme projects a body that is taller than the body box
- **THEN** the deck has at least two slides
- **AND** each slide body measures no taller than the usable body box

### Requirement: Region boxes clip their contents

Title, body, and footer regions MUST clip painting to their box so glyphs and outlines cannot draw outside the lower-third band.

#### Scenario: Regions clip

- **WHEN** a Lower Third slide is on the projector
- **THEN** each text region clips to its width and height
