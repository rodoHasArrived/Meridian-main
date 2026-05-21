---
name: workstation-screen-composition
description: Compose Meridian desktop workstation screens from shared shell, navigation, layout, command, status, table, inspector, and diagnostics primitives. Use when adding or reshaping WPF workspaces, tabs, panels, toolbar actions, operator briefings, or screen-level layout.
---

# Workstation Screen Composition

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Create workstation screens that feel like one platform: consistent navigation, commands, status,
grid/inspector flow, diagnostics, and recovery behavior.

## Inputs Required

- Workspace name, domain area, primary operator task, and navigation target.
- Existing shell route, shared controls, styles, templates, and tests.
- Data density, update cadence, and expected detail/inspector tabs.

## Use When

Use this skill before adding a new WPF workspace or major screen composition.

Trigger examples:

- "Create a Provider Management workspace."
- "Add a dense table with a detail pane."
- "Reshape this desktop screen into workstation layout."

## Do Not Use When

Use `modular-desktop-mvvm` for small MVVM implementation changes and
`dense-data-grid-inspector-panel` for grid-specific implementation details.

Non-trigger examples:

- "Fix this one command disabled state."
- "Only write tests for an existing view model."
- "Audit performance in this service."

## Workflow

1. Inventory existing workspace routes, shell chrome, shared panels, command bars, status badges,
   tables, inspectors, and test patterns.
2. Define the screen contract: primary grid or summary, detail tabs, command groups, diagnostics,
   loading/error/empty states, and navigation handoffs.
3. Reuse shared primitives first; create a shared primitive only when the layout will repeat.
4. Keep screen-level XAML shallow; put state and workflow projection in the view model.
5. Validate data volume assumptions and choose virtualization, paging, or bounded preview.
6. Add tests for view-model state and shell/navigation registration.

## Output Expectations

- A screen structure with reusable sections and clear ownership.
- Navigation and command behavior documented in code/tests.
- Resource and responsiveness assumptions called out.

## Files Likely Affected

- `src/Meridian.Wpf/Shell/`, `Views/`, `ViewModels/`, `Controls/`, `Templates/`, `Styles/`
- `tests/Meridian.Wpf.Tests/Shell/`, `ViewModels/`, `Views/`

## Architecture Rules

- Keep top-level navigation aligned to Meridian's workstation taxonomy.
- Do not create isolated screen-only versions of standard command bars, status rows, grids, or
  diagnostic panels.
- Prefer view-model-owned screen sections over view-owned branching logic.

## Testing Requirements

- Cover route/catalog registration for new workspaces.
- Cover loading, empty, partial, error, and selected-detail states.
- Cover command availability and navigation handoffs.

## Common Mistakes To Avoid

- Building a decorative page instead of an operator workflow.
- Nesting card-like panels until dense workstation tasks become hard to scan.
- Recomputing entire screen state for every live update.
- Leaving new screen composition untested because it "only" changes XAML.

## Resource Management Considerations

- Use stable layout dimensions for dense tables and command areas.
- Throttle live telemetry, provider, and diagnostics updates.
- Avoid eager detail-panel loading until a row or tab needs it.

## Handoffs

- Hand off to `shared-component-extraction` when a pattern should become a primitive.
- Hand off to `diagnostics-audit-timeline` for evidence or audit-heavy panels.
- Hand off to `desktop-test-generation` for focused WPF coverage.

## Validation

- Run focused shell/navigation and affected view-model tests.
- Run `pwsh ./tools/codex/component-inventory.ps1` before adding new shared controls.
- Run `git diff --check -- <changed files>`.

## Output Standards

- Name reused workstation primitives and any new primitive introduced.
- Explain data-volume handling and command-state ownership.
- Report tests and any missing visual or automation evidence.
