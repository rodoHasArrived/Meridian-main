---
name: meridian-cleanup
description: Clean up Meridian code and docs without changing observable behavior. Use when the user asks to clean up, tidy, remove dead code, reduce duplication, fix safe anti-patterns, remove stale docs, or make focused maintainability improvements in Meridian C#, F#, WPF, or documentation files.
---

# Meridian Cleanup

Make Meridian easier to maintain without turning a cleanup pass into a feature, refactor,
or compliance sweep.

Read `../_shared/project-context.md` before editing. Use [`.github/agents/cleanup-agent.md`](../../../.github/agents/cleanup-agent.md) as the canonical cleanup spec when the task is not trivially local.

## Workflow

1. Triages the request into `default-safe`, `guarded`, or `handoff` using [`.github/agents/cleanup-agent.md`](../../../.github/agents/cleanup-agent.md).
2. Reads the touched files fully and gathers the evidence that matches the surface: call sites, tests, bindings, generated files, DI registration, or docs links.
3. Applies the smallest reviewable change set that preserves behavior.
4. Runs the narrowest relevant validation command for the chosen lane.
5. Reports the cleanup using the shared output contract or hands off when the request is not actually cleanup.

## Lane Summary

- `default-safe`: dead private code with evidence, duplicate `using` / `open`, inert code tombstones, low-risk docs cleanup, unused `x:Name` / empty handlers, obvious structured-log template fixes, and low-risk log-noise cleanup.
- `guarded`: still behavior-preserving, but touches reflection, DI, source-generated serialization, XAML bindings, hot paths, or a small multi-file surface. Requires extra evidence and stronger validation.
- `handoff`: public contract changes, `IOptions<T>` to `IOptionsMonitor<T>`, `HttpClientFactory` migrations, repo-wide CPM sweeps, provider attribute/compliance work, caller-updating obsolete cleanup, or architectural refactors framed as cleanup.

## Guardrails

- Preserve behavior unless the user explicitly asks for functional change.
- Do not remove code that may be used indirectly by reflection, DI, source generation, attributes, or XAML bindings without evidence.
- Do not change public contracts, ADR traceability attributes, cancellation flow, or project boundaries as part of cleanup.
- Keep large cleanup requests theme-based; avoid mixing unrelated edits in one pass.
- Prefer notes and rerouting when a "cleanup" request is really a risky refactor or compliance pass.

## Validation Guidance

- Named-file cleanup: use local evidence plus the narrowest relevant build, test, or doc check.
- Repo-wide cleanup: prefer targeted updater commands such as `audit-code`, `audit-docs`, or `audit-config` before full `audit`.
- Reserve full `verify` for broad cleanup passes, not every small cleanup theme.
- In PowerShell, set `$env:PYTHONIOENCODING='utf-8'` before running `ai-repo-updater.py`.
- Optional helper for repo-local PowerShell sessions: [`scripts/repo-updater.ps1`](scripts/repo-updater.ps1)

## Meridian-Specific Rules

- Treat `[DataSource]`, `[ImplementsAdr]`, `[StorageSink]`, `[JsonSerializable]`, and provider contracts as high-risk surfaces.
- Route storage-related cleanup through existing WAL and `AtomicFileWriter` expectations rather than "simplifying" durability away.
- Keep hot-path cleanup mindful of `EventPipelinePolicy`, cancellation, and structured logging.
- When cleanup touches providers, verify options, resilience, and JSON-context rules still match neighboring implementations.

## Output Standards

- Use the shared output contract from [`.github/agents/cleanup-agent.md`](../../../.github/agents/cleanup-agent.md): lane, cleanup theme, planned changes, safety evidence, validation, and residual risk or handoff.
- State the cleanup theme in one line before editing.
- Summarize what was removed or simplified and why it was safe.
- Mention the exact validation command run, or say explicitly when the task was handed off instead of edited.
