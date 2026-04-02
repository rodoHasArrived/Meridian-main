---
name: meridian-cleanup
description: Clean up Meridian code and docs without changing observable behavior. Use when the user asks to clean up, tidy, remove dead code, reduce duplication, fix safe anti-patterns, remove stale docs, or make focused maintainability improvements in Meridian C#, F#, WPF, or documentation files.
---

# Meridian Cleanup

Make Meridian easier to maintain without turning a cleanup pass into a feature, architecture, or behavior change.

Read `../_shared/project-context.md` before editing. If the cleanup touches specialized rules, inspect the nearest source, tests, and existing agent guidance in `.github/agents/cleanup-agent.md`.

## Workflow

1. Confirm the cleanup scope from the prompt or infer the smallest safe target area.
2. Read the touched files fully and check nearby call sites, tests, bindings, DI registration, and serialization usage before removing anything.
3. Pick the cleanup category: dead code, duplication, safe anti-pattern, log hygiene, stale docs, or code-behind noise.
4. Apply the smallest reviewable change set that preserves behavior.
5. Run the narrowest relevant validation command and report any remaining risk.

## Guardrails

- Preserve behavior unless the user explicitly asks for functional change.
- Do not remove code that may be used indirectly by reflection, DI, source generation, attributes, or XAML bindings without evidence.
- Do not change public contracts, ADR traceability attributes, cancellation flow, or project boundaries as part of cleanup.
- Keep large cleanup requests theme-based; avoid mixing unrelated edits in one pass.
- Prefer notes over risky rewrites when a "cleanup" request is really an architectural refactor.

## Cleanup Lenses

- Dead code: remove unused private members, stale comments, tombstones, and unreachable branches after checking references.
- Duplication: consolidate truly identical private logic and repeated guards, but not code with subtly different semantics.
- Safe anti-patterns: fix structured logging issues, async blocking, obvious package-version drift, and other repository rule violations when behavior remains unchanged.
- Documentation: fix stale paths, broken links, duplicate sections, and formatting drift when the code reality is clear.
- WPF cleanup: remove unused `x:Name`, empty handlers, duplicate usings, and inert code-behind noise without moving behavior into new layers.

## Meridian-Specific Rules

- Treat `[DataSource]`, `[ImplementsAdr]`, `[StorageSink]`, `[JsonSerializable]`, and provider contracts as high-risk surfaces.
- Route storage-related cleanup through existing WAL and `AtomicFileWriter` expectations rather than "simplifying" durability away.
- Keep hot-path cleanup mindful of `EventPipelinePolicy`, cancellation, and structured logging.
- When cleanup touches providers, verify options, resilience, and JSON-context rules still match neighboring implementations.

## Output Standards

- State the cleanup theme in one line before editing.
- Summarize what was removed or simplified and why it was safe.
- Mention the exact validation command run, or say explicitly if validation could not be completed.
