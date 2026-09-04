## Purpose

Keeps the Settings dialog a fixed-size card layout so every row and control fits inside the content pane.

## ADDED Requirements

### Requirement: Form fits the content pane

The Settings form width SHALL be less than or equal to the content pane width, computed as window width minus sidebar width minus horizontal content padding. The window SHALL remain a fixed size and MUST NOT be resizable.

#### Scenario: Pane math leaves no overflow

- **WHEN** the Settings chrome constants are applied
- **THEN** form width is less than or equal to window width minus sidebar width minus content padding
- **AND** the window is not resizable

### Requirement: Setting rows use a reserved control column

Each Display, Behavior, and About setting row SHALL place the label and hint in a flexible column and the editor in a reserved control column so editors stay aligned and do not sit on the window’s far edge.

#### Scenario: Combo and toggle columns are narrower than the form

- **WHEN** a combo row and a toggle row are measured against the form width
- **THEN** the combo column is 200 wide and the toggle column is 40 wide
- **AND** both are strictly narrower than the form

### Requirement: On/off editors are compact

On/off settings in Settings SHALL use a 40-by-22 compact switch. The system MUST NOT use a full Fluent toggle switch for those rows.

#### Scenario: Toggle column matches the compact switch

- **WHEN** an on/off setting is shown
- **THEN** its reserved column is 40 wide

### Requirement: Screen outputs are a single row

Each screen output SHALL show the on/off switch, name, and theme editor on one row, the same arrangement as before the card pass.

#### Scenario: Screens use a single row

- **WHEN** the Screens page lists outputs
- **THEN** the layout mode is a single row
- **AND** switch + theme columns plus a minimum name column still fit inside the form width

### Requirement: Sidebar items show an icon and a label

Each Settings sidebar item SHALL show both an icon and a text label.

#### Scenario: Every nav item has an icon kind

- **WHEN** the sidebar items are listed
- **THEN** each item has a non-empty icon identifier and a non-empty label

### Requirement: Settings pages scroll inside the dialog

Each Settings page SHALL scroll vertically inside the content pane. The scroll viewport height SHALL be the window height minus the footer height, so a long Screens list is not clipped.

#### Scenario: Content pane is shorter than the window

- **WHEN** the Settings chrome constants are applied
- **THEN** content pane height equals window height minus footer height
- **AND** that height is less than the window height
