# Meridian Codex Desktop Workstation Instructions

Use this file for Codex-specific desktop implementation behavior. Keep repository-wide guidance in
the root `AGENTS.md`, `CLAUDE.md`, and `.codex/skills/_shared/project-context.md`.
For multi-lane AI work, route coordination and handoff format through
`docs/ai/agent-handoff-checklist.md` and keep host-specific Codex docs aligned to it.
Select a mode from `docs/ai/work-modes.md` before implementation and initialize
`docs/ai/parallel-task-manifest-template.md` before concurrent lane execution.

## Default Implementation Rules

- Prefer reusable modules over screen-specific implementations.
- Use MVVM consistently: views declare UI, view models own presentation state, services own workflow
  and provider orchestration.
- Keep business logic, provider calls, file I/O, and validation rules out of XAML code-behind.
- Use shared workstation primitives before creating new controls, commands, models, or services.
- Extract repeated layout, dense-table, status, command, empty-state, loading, error, diagnostics,
  and inspector patterns when a second real usage appears.
- Preserve existing behavior unless the user explicitly asks for behavior change.
- Avoid large risky rewrites. Prefer incremental changes with narrow tests and a rollback path.
- Add or update tests for every meaningful view model, command, service, mapping, or workflow change.
- Avoid unnecessary package dependencies and one-off framework choices.
- Document assumptions and tradeoffs in the final response or the nearest design doc when they
  affect architecture, performance, resource use, or operator workflow.

## Internal Design Questions

Before editing, answer these internally:

1. Can this be a shared component?
2. Can this be a shared view model?
3. Can this be a shared command?
4. Can this reuse an existing service?
5. Can this be tested without the UI?
6. Can this scale to large data?
7. Can this avoid unnecessary memory or CPU use?
8. Can this be safely refactored later?

## Resource Guardrails

- Memory: use virtualization for large grids, avoid full materialization, prefer paging or streaming,
  avoid duplicate in-memory copies, dispose unmanaged resources, and unsubscribe from events.
- CPU: debounce expensive filters, batch high-frequency UI updates, cache calculated values only with
  clear invalidation, and move heavy work off the UI thread.
- I/O: avoid synchronous file, database, and network I/O on UI paths; use async APIs, cancellation
  tokens, bounded retries, and batching where appropriate.
- Rendering: avoid rebuilding full visual trees for live data, throttle telemetry updates, and use
  lightweight row view models for dense tables.
- Concurrency: make long operations cancelable, prevent duplicate command execution, avoid
  unsupervised fire-and-forget tasks, and surface progress and errors to the operator.
- Lifecycle: dispose closeable view models, stop subscriptions when panels close, release provider
  connections, clean temporary research datasets, and define cache retention policies.

## Required Skill Routing

- Use `modular-desktop-mvvm` for new or changed WPF view/view-model/service implementation.
- Use `workstation-screen-composition` before adding a new workspace or large screen.
- Use `shared-component-extraction` when repeated UI or view-model patterns appear.
- Use `provider-management-workflow` for provider setup, credentials, health, degradation, or
  validation workflows.
- Use `research-data-acquisition` for research ingestion, symbol discovery, backfill, import, or
  dataset-preparation workflows.
- Use `dense-data-grid-inspector-panel` for dense tables, grid selection, details, and inspector
  panels.
- Use `diagnostics-audit-timeline` for diagnostics, timeline, evidence, or audit surfaces.
- Use `performance-resource-review` before finalizing broad UI, provider, data, or workflow changes.
- Use `safe-refactoring` for behavior-preserving consolidation or cleanup.
- Use `desktop-test-generation` when adding or repairing WPF tests.

## Local Quality Scripts

Run focused scripts from the repository root:

```powershell
pwsh ./tools/codex/run-codex-quality-suite.ps1 -Fast -MarkdownPath artifacts/codex/codex-quality-suite.fast.md
pwsh ./tools/codex/run-codex-quality-suite.ps1 -MarkdownPath artifacts/codex/codex-quality-suite.md
pwsh ./tools/codex/architecture-scan.ps1 -MarkdownPath artifacts/codex/architecture-scan.md
pwsh ./tools/codex/mvvm-compliance-check.ps1
pwsh ./tools/codex/resource-review.ps1
```

Generator scripts are dry-run by default. Use `-Apply` only after reviewing the planned output.
