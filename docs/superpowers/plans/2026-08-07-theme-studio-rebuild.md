# Theme Studio Rebuild — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild Theme Studio UI with a context-sensitive inspector, scrollable sidebar, and split UserControls — no duplicate properties across tabs.

**Architecture:** Keep `ThemeStudioViewModel` logic; add `InspectorTarget` enum; split `ThemeStudioWindow.axaml` into Sidebar / Canvas / Inspector UserControls; replace TabControl with Expander sections gated by selection.

**Tech Stack:** Avalonia 11.3, ReactiveUI, existing Theme models

## Global Constraints

- Avalonia 11.3; do not change `Theme` persistence schema.
- Operator palette: `#0E1014`, `#181C24`, `#7C6CF6`, `#2A303C`.
- Dialog shell retained; min window 1180×720.
- `MinWidth="0"` on inspector text fields; inspector content in `ScrollViewer`.

---

### Task 1: InspectorTarget in ViewModel

**Files:**
- Modify: `src/ChurchProjection.UI/ViewModels/ThemeStudioViewModel.cs`

**Interfaces:**
- Produces: `InspectorTarget`, `ShowThemeInspector`, `ShowRegionInspector`, `ShowShapeInspector`, `SelectThemeBackground()`

- [ ] Add `InspectorTarget` enum and selection routing
- [ ] Update `SelObjectName` for theme level ("Theme")
- [ ] Clear layer list selection when theme-level

### Task 2: UserControl files

**Files:**
- Create: `src/ChurchProjection.UI/Views/ThemeStudio/ThemeStudioSidebar.axaml(.cs)`
- Create: `src/ChurchProjection.UI/Views/ThemeStudio/ThemeStudioCanvas.axaml(.cs)`
- Create: `src/ChurchProjection.UI/Views/ThemeStudio/ThemeStudioInspector.axaml(.cs)`

### Task 3: Slim window shell

**Files:**
- Modify: `src/ChurchProjection.UI/Views/ThemeStudioWindow.axaml(.cs)`

- [ ] Host three UserControls; simplified toolbar with Add flyout
- [ ] Move canvas/sidebar event handlers to child code-behind

### Task 4: Verify

- [ ] `dotnet build src/ChurchProjection.App/ChurchProjection.App.csproj`
- [ ] Manual smoke: open Theme Studio, switch selections, resize window
