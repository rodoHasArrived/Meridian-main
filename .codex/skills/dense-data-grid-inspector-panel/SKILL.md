---
name: dense-data-grid-inspector-panel
description: Implement Meridian WPF dense data grids and inspector panels with virtualization, lightweight row view models, stable selection, detail tabs, command state, and scalable resource behavior. Use for tables, blotters, provider grids, research previews, ledgers, and diagnostic lists.
---

# Dense Data Grid And Inspector Panel

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Build dense operator tables and detail panels that remain usable and responsive with large or
frequently updated datasets.

## Inputs Required

- Dataset shape, approximate row count, update cadence, sort/filter needs, and selected-row behavior.
- Existing table controls, templates, row view models, inspector patterns, and tests.
- Required commands, detail tabs, and empty/error states.

## Use When

Use this skill for WPF grids, lists, inspector panes, blotters, previews, and dense operational data.

Trigger examples:

- "Add a dense positions grid with detail tabs."
- "Improve this table selection and inspector."
- "Virtualize the provider events list."

## Do Not Use When

Use `workstation-screen-composition` for whole-screen layout and `performance-resource-review` for
resource audit without implementation.

Non-trigger examples:

- "Add a provider credential form."
- "Write tests only."
- "Review architecture docs."

## Workflow

1. Inventory existing grid/list controls, row models, column templates, inspectors, and tests.
2. Decide virtualization, paging, incremental update, and selection preservation strategy.
3. Use lightweight row view models and avoid copying full domain objects into every row.
4. Keep filtering/search debounced and cancelable when expensive.
5. Put selected-row detail projection in a view model; load heavy detail tabs lazily.
6. Add tests for empty, loading, selected, stale, filtered, error, and command states.

## Output Expectations

- A grid and inspector that scale to expected data volume.
- Stable selected-row behavior and bounded detail loading.
- Tests and resource notes.

## Files Likely Affected

- `src/Meridian.Wpf/Controls/`, `Views/`, `ViewModels/`, `Templates/`, `Styles/`
- `tests/Meridian.Wpf.Tests/ViewModels/`, `Views/`

## Architecture Rules

- Do not place data transformation or command decisions in cell templates.
- Keep row models lightweight and immutable where practical.
- Use shared table, status, and inspector primitives before creating new variants.

## Testing Requirements

- Cover selection persistence across refresh/filter.
- Cover command state for selected and no-selection cases.
- Cover large fixture sizes where a resource risk is being fixed.

## Common Mistakes To Avoid

- Binding a dense grid to a fully materialized, duplicated dataset.
- Rebuilding all rows for small live updates.
- Loading every detail tab for every row.
- Adding expensive converters in hot cell templates.

## Resource Management Considerations

- Enable virtualization and avoid dynamic layout that disables it.
- Use incremental updates and bounded collections for live feeds.
- Debounce filters and throttle high-frequency updates.
- Release subscriptions when the grid view model is disposed.

## Handoffs

- Hand off to `shared-component-extraction` if grid/inspector patterns repeat.
- Hand off to `performance-resource-review` for high-frequency or very large datasets.
- Hand off to `desktop-test-generation` for view-model and binding tests.

## Validation

- Run focused WPF tests for the affected view model or view.
- Run `pwsh ./tools/codex/resource-review.ps1`.
- Run `git diff --check -- <changed files>`.

## Output Standards

- State row-count assumptions, virtualization/paging choice, and selection behavior.
- Report tests and remaining rendering or performance risks.
