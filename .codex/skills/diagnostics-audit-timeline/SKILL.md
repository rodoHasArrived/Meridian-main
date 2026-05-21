---
name: diagnostics-audit-timeline
description: Implement Meridian WPF diagnostics panels, audit timelines, evidence trails, activity histories, health triage, replay evidence, and operator recovery surfaces with stable ordering, lifecycle cleanup, and testable view models.
---

# Diagnostics And Audit Timeline

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Expose system and workflow evidence so operators can understand what happened, what is stale or
degraded, and what action is safe next.

## Inputs Required

- Diagnostic or audit source, event schema, retention, update cadence, and operator actions.
- Existing activity log, notification, provider health, system health, replay, reconciliation, and
  evidence models.
- Required filters, grouping, detail view, export, or recovery commands.

## Use When

Use this skill for diagnostics, audit history, evidence trails, and timeline UI.

Trigger examples:

- "Add an audit timeline to reconciliation."
- "Build a provider diagnostics panel."
- "Show replay evidence history in WPF."

## Do Not Use When

Use `provider-management-workflow` for provider setup actions and `dense-data-grid-inspector-panel`
for generic table/inspector work without diagnostics semantics.

Non-trigger examples:

- "Add a plain editable form."
- "Only scan for resource issues."
- "Create a new provider adapter."

## Workflow

1. Inventory existing evidence, activity, notification, health, replay, audit, and reconciliation
   models before adding a new timeline model.
2. Define event ordering, timestamp source, grouping, severity, freshness, retention, and recovery
   actions.
3. Keep evidence retrieval and aggregation in services; keep projection and command state in view
   models.
4. Use paging/virtualization for long histories and throttle live updates.
5. Add tests for ordering, grouping, stale evidence, empty state, failure, selection, and recovery.

## Output Expectations

- Diagnostics or timeline surface with traceable evidence and stable ordering.
- View model and service tests.
- Resource and retention assumptions documented.

## Files Likely Affected

- `src/Meridian.Wpf/ViewModels/`, `Views/`, `Services/`, `Controls/`
- `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/`, domain service projects as needed
- `tests/Meridian.Wpf.Tests/`, `tests/Meridian.Tests/`

## Architecture Rules

- Do not invent duplicate audit/event DTOs when shared evidence models exist.
- Keep operator-facing explanations deterministic and testable.
- Keep export/report actions gated and explicit.

## Testing Requirements

- Cover timestamp ordering and stable sort ties.
- Cover stale, degraded, failed, empty, loading, and recovery states.
- Cover no-secret/no-sensitive-data projection where provider/account events are shown.

## Common Mistakes To Avoid

- Polling diagnostics too frequently.
- Showing raw logs instead of curated operator evidence.
- Losing correlation IDs or source references.
- Retaining event subscriptions after a panel closes.

## Resource Management Considerations

- Page or virtualize long timelines.
- Use bounded retention and explicit cache cleanup.
- Throttle live telemetry and avoid repeated expensive aggregation.
- Dispose subscriptions and timers.

## Handoffs

- Hand off to `performance-resource-review` for telemetry-heavy surfaces.
- Hand off to `shared-component-extraction` when timeline/status patterns repeat.
- Hand off to `desktop-test-generation` for WPF coverage.

## Validation

- Run focused tests for evidence aggregation and WPF projection.
- Run `pwsh ./tools/codex/resource-review.ps1` when live updates, timers, or subscriptions change.
- Run `git diff --check -- <changed files>`.

## Output Standards

- State evidence source, ordering, retention, update cadence, and tests.
- Name any missing live diagnostic proof or privacy risk.
