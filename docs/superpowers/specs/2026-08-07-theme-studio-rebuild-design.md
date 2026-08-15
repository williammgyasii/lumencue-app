# Theme Studio Rebuild — Design Spec

**Date:** 2026-08-07  
**Status:** Approved

## Problem

Theme Studio grew feature-by-feature into an inconsistent UI:

- **Content / Layout / Theme tabs** duplicate properties (background, colors, images appear in multiple places).
- **Left sidebar** crams themes, objects, and assignments into one `DockPanel` without proper scroll regions.
- **Fixed 360px inspector** clips `ColorPicker`, sliders, and path textboxes.
- **Toolbar** duplicates z-order actions already in the inspector.

## Goals

1. Each property appears in **exactly one place**.
2. Layout matches the main operator window (colors, section labels, spacing).
3. No overflow/clipping at minimum window size (1180×720).
4. Keep the dialog shell and all existing capabilities.

## Non-Goals (v1)

- Moving Theme Studio into the main operator window as a mode.
- Removing import-lower-third, live preview, or content-type assignments.
- Rewriting theme persistence or `ProjectorView` rendering.

## Architecture

### Shell (unchanged concept)

Separate maximized dialog window with three columns:

| Column | Width | Content |
|--------|-------|---------|
| Left | 240px fixed | Themes list, layers list, assign-to-content |
| Center | `*` flex | 16:9 canvas with selection overlay |
| Right | 380px min | Context-sensitive inspector |

### Inspector model (context-sensitive, no tabs)

| Selection | Inspector shows |
|-----------|-----------------|
| **Theme** (click empty canvas) | Background, key color, global legibility, layout preset |
| **Text region** (Title/Body/Footer) | Text, caption box, position, alignment |
| **Shape** | Fill, image, opacity, z-order, position |

Toolbar: theme name, **Add** flyout (rectangle / bar / import), **Delete**, **Save**. Z-order only in shape inspector.

### File split

| File | Responsibility |
|------|----------------|
| `ThemeStudioWindow.axaml` | Shell grid, toolbar, hosts child controls |
| `ThemeStudioSidebar.axaml` | Themes + layers + assignments (each scrollable) |
| `ThemeStudioCanvas.axaml` | Preview, overlay, drag/resize handles |
| `ThemeStudioInspector.axaml` | Expander-based inspector panels |
| `ThemeStudioViewModel.cs` | Draft editing logic (kept, add `InspectorTarget`) |

### ViewModel additions

```csharp
enum InspectorTarget { Theme, Region, Shape }
void SelectThemeBackground(); // click canvas → theme-level inspector
bool ShowThemeInspector / ShowRegionInspector / ShowShapeInspector
```

## Global constraints

- Avalonia 11.3, existing `Theme` / `ThemeShape` / `ThemeRegion` models unchanged.
- Match operator palette: `#0E1014` bg, `#181C24` panels, `#7C6CF6` accent, `#2A303C` borders.
- Inspector controls: `MinWidth="0"` on text fields; `ScrollViewer` wraps all inspector content.
- Canvas remains 1920×1080 design space scaled to viewport.

## Testing

Manual:

1. Open Theme Studio → no clipped controls at 1180×720.
2. Click canvas background → theme inspector only (no region/shape sections).
3. Select Body → text + layout sections; theme background not duplicated.
4. Select shape → fill/z-order; no text controls.
5. Songs → Media style: left sidebar sections scroll independently.
6. Save theme → assignments and preview unchanged.
