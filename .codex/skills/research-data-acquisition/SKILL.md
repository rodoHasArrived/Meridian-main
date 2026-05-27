---
name: research-data-acquisition
description: Implement Meridian desktop research-data acquisition workflows for provider-backed import, preview, validation, lineage, catalog handoff, backfill, and dataset cleanup. Use for WPF research acquisition screens, acquisition services, and view models.
---

# Research Data Acquisition

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Build research acquisition flows that collect useful data without overloading memory, hiding
lineage, or duplicating provider/storage models.

## Inputs Required

- Data source, symbols/assets, date range, granularity, preview size, and handoff target.
- Existing provider, backfill, ETL, catalog, storage, lineage, and tests.
- Expected dataset size and retention/cleanup needs.

## Use When

Use this skill for desktop workflows that acquire, preview, validate, or prepare research data.

Trigger examples:

- "Add a research import workflow."
- "Build a backfill preview screen."
- "Add acquisition validation and lineage to WPF."

## Do Not Use When

Use `provider-management-workflow` for provider setup/health and `dense-data-grid-inspector-panel`
for grid-only work after acquisition state already exists.

Non-trigger examples:

- "Add a provider credentials page."
- "Only refactor an existing table control."
- "Review tests for a command."

## Workflow

1. Inventory provider/backfill services, ETL commands, catalog/storage seams, research view models,
   and tests.
2. Define bounded preview, validation, lineage, retry, cancel, partial-data, and cleanup behavior.
3. Keep acquisition orchestration in services and operator projection in view models.
4. Use async streaming or paging where practical; avoid full materialization of large datasets.
5. Persist provenance, source, freshness, schema, and validation results through existing seams.
6. Add tests for invalid input, cancel, retry, partial success, provider failure, and successful handoff.

## Output Expectations

- Acquisition flow with clear provider reuse and dataset lifecycle.
- View model states for loading, preview, validation, partial, error, and completed states.
- Tests and validation evidence.

## Files Likely Affected

- `src/Meridian.Wpf/ViewModels/`, `Views/`, `Services/`
- `src/Meridian.Application/`, `src/Meridian.Infrastructure/`, `src/Meridian.Storage/`
- `tests/Meridian.Wpf.Tests/`, `tests/Meridian.Tests/`

## Architecture Rules

- Do not duplicate provider/backfill/storage DTOs for a screen.
- Keep import validation reusable and testable outside the UI.
- Preserve catalog, lineage, and storage boundaries.

## Testing Requirements

- Use bounded fixture datasets.
- Cover cancellation and partial-data recovery.
- Cover validation output and handoff metadata.

## Common Mistakes To Avoid

- Loading an entire history into `ObservableCollection<T>`.
- Losing provenance during preview transformation.
- Blocking the UI while parsing or validating data.
- Leaving temporary datasets without cleanup or retention rules.

## Resource Management Considerations

- Use streaming/paging for large acquisition.
- Keep previews bounded and clearly labeled.
- Clean temporary datasets and release file/provider handles.
- Use cancellation tokens and bounded retries.

## Handoffs

- Hand off to `dense-data-grid-inspector-panel` for preview/inspector UI implementation.
- Hand off to `performance-resource-review` for large acquisition flows.
- Hand off to `desktop-test-generation` for scenario coverage.

## Validation

- Run focused acquisition/service tests and affected WPF view-model tests.
- Run `pwsh ./tools/codex/resource-review.ps1`.
- Run `git diff --check -- <changed files>`.

## Output Standards

- State data-volume limits, lineage handling, cleanup policy, and tests.
- Name any provider behavior that was mocked rather than live-validated.
