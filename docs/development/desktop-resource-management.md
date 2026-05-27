# Desktop Resource Management

Meridian desktop workflows must be designed for long-running workstation sessions, large financial
datasets, live provider state, and operator trust. Resource management is part of correctness.

## Memory

- Use virtualization for large grids and lists.
- Avoid full materialization of large provider, research, ledger, or diagnostics datasets.
- Prefer paging, streaming, and bounded previews.
- Avoid duplicate in-memory copies of domain data and row view models.
- Dispose unmanaged resources, streams, provider connections, timers, and subscriptions.
- Use explicit unsubscribe or weak-event patterns where view models can outlive views.

## CPU

- Debounce expensive search and filters.
- Batch high-frequency updates when the operator does not need tick-level redraws.
- Cache calculated values only with clear invalidation rules.
- Avoid recomputing entire screen projections for small live updates.
- Move heavy work to background services and surface progress.

## I/O

- Do not perform synchronous file, database, or network I/O on UI workflows.
- Use async APIs with cancellation tokens.
- Batch writes where appropriate.
- Use bounded retry/backoff for external providers.
- Avoid noisy repeated calls and unsupervised polling.

## UI Rendering

- Keep dense table row models lightweight.
- Preserve virtualization; avoid layout choices that disable it.
- Avoid excessive bindings and expensive converters in hot cell templates.
- Update incrementally where practical.
- Load heavy inspector details lazily.

## Concurrency And Lifecycle

- Avoid fire-and-forget tasks unless they are deliberately supervised.
- Prevent duplicate async command execution.
- Make long-running operations cancelable.
- Dispose view models when workspaces close.
- Stop subscriptions when panels close.
- Clean temporary research datasets and define cache retention policies.

Run:

```powershell
pwsh ./tools/codex/resource-review.ps1 -MarkdownPath artifacts/codex/resource-review.md
```

Treat the output as review leads, not automatic truth.
