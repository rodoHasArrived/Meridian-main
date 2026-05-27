---
name: performance-resource-review
description: Review Meridian desktop, provider, research, diagnostics, and workflow changes for memory, CPU, I/O, rendering, concurrency, lifecycle, and computer resource risks. Use before finalizing broad WPF changes, data-heavy screens, polling, timers, async workflows, or refactors.
---

# Performance And Resource Review

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Find and reduce practical workstation resource risks before they become sluggish screens, memory
growth, provider noise, or brittle async behavior.

## Inputs Required

- Target files or diff and expected data volume/update cadence.
- Known performance complaint or resource objective when available.
- Existing tests, benchmarks, scripts, and validation constraints.

## Use When

Use this skill for resource-sensitive implementation or review.

Trigger examples:

- "Review this WPF change for memory issues."
- "Optimize this dense table."
- "Check for blocking UI-thread work."

## Do Not Use When

Use `safe-refactoring` for behavior-only cleanup and `dense-data-grid-inspector-panel` when actively
implementing a grid.

Non-trigger examples:

- "Add a new prompt template."
- "Plan a roadmap."
- "Only fix a typo in docs."

## Workflow

1. Inspect target files for synchronous I/O, blocking waits, `Thread.Sleep`, unbounded collections,
   repeated timers, event retention, high-frequency updates, and full materialization.
2. Run `resource-review.ps1` and use findings as leads, not automatic truth.
3. Identify the smallest change that reduces the measured or plausible bottleneck.
4. Preserve behavior with tests or characterization evidence.
5. Validate with focused tests and, where practical, before/after measurements or fixture sizes.

## Output Expectations

- Findings separated by verified issue, inferred risk, and acceptable tradeoff.
- Small fixes or a ranked plan when implementation is not requested.
- Tests and resource evidence.

## Files Likely Affected

- `src/Meridian.Wpf/`, `src/Meridian.Application/`, `src/Meridian.Infrastructure/`,
  `src/Meridian.Storage/`, `src/Meridian.Ui.Services/`
- `tests/`, `benchmarks/`, `tools/codex/`

## Architecture Rules

- Do not optimize by bypassing MVVM, provider, storage, or shared read-model boundaries.
- Prefer bounded, cancelable, incremental work over global caches or broad rewrites.
- Keep cache invalidation explicit.

## Testing Requirements

- Cover behavior before and after optimization.
- Add regression tests for cancellation, bounded results, throttling, or duplicate execution.
- Use benchmarks only when a unit/integration test cannot prove the risk.

## Common Mistakes To Avoid

- Treating heuristic scan output as a confirmed bug without reading code.
- Adding global caches without invalidation.
- Moving work to the background without cancellation or error surfacing.
- Ignoring lifecycle cleanup for timers/subscriptions.

## Resource Management Considerations

- Memory: virtualize, page, stream, avoid duplicate materialization, dispose resources.
- CPU: debounce, batch, cache carefully, avoid repeated recomputation.
- I/O: use async, cancellation, batching, and bounded retry/backoff.
- Rendering: use incremental updates and lightweight row view models.
- Concurrency: avoid fire-and-forget; prevent duplicate async command execution.

## Handoffs

- Hand off to `safe-refactoring` when resource fixes require multi-step extraction.
- Hand off to `desktop-test-generation` for regression tests.
- Hand off to domain skills when a provider or acquisition workflow needs redesign.

## Validation

- Run `pwsh ./tools/codex/resource-review.ps1`.
- Run focused tests for behavior and async/resource changes.
- Run `git diff --check -- <changed files>`.

## Output Standards

- State bottleneck evidence, fix, validation, and residual risk.
- Distinguish measured facts from heuristic or inferred risks.
