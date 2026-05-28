---
name: meridian-code-review
description: Review Meridian code for bugs, regressions, architecture drift, missing tests, and project-rule violations. Use when the user asks for a review, audit, refactor assessment, compliance check, bug-risk scan, or improvement pass on Meridian C#, F#, TypeScript/React, XAML, provider, storage, pipeline, execution, ledger, browser dashboard, or WPF desktop code.
---

# Meridian Code Review

Review Meridian changes with a bug-finding mindset first. Prefer findings over summary, and ground every finding in Meridian's architecture rules.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before reviewing.
If the change touches a highly specialized area, inspect the relevant source files and tests before
drawing conclusions.

## Use When

Use this skill when the requested output is review, audit, bug-risk assessment, architecture
compliance, missing-test analysis, or a findings-first critique.

Trigger examples:

- "Review this dashboard change for regressions."
- "Audit the provider implementation for contract drift."
- "Scan this refactor for bugs and missing tests."

## Do Not Use When

Use `meridian-cleanup` when the user wants behavior-preserving edits, `meridian-test-writer` when
the main request is to add tests, and `meridian-implementation-assurance` when implementation plus
evidence is required.

Non-trigger examples:

- "Clean up this file now."
- "Write regression tests for this bug."
- "Implement the approved fix and verify it."

## Workflow

1. Understand what changed and why.
2. Map cross-file relationships before evaluating individual lines.
3. Prioritize correctness, regressions, and contract violations.
4. Check for missing or weak tests after code-level risks.
5. Keep summaries brief; findings come first.

## Handoffs

- Hand off to `meridian-test-writer` when the main residual risk is missing scenario coverage.
- Hand off to `meridian-cleanup` when the review finds safe maintainability-only cleanup.
- Hand off to `meridian-implementation-assurance` when the user asks to fix findings and gather proof.

## Validation

- Prefer reading the actual diff, nearby tests, contracts, and docs over speculation.
- Run narrow tests only when the user asks for verification or when a small command materially improves confidence.
- Report validation gaps explicitly if the review is static-only.

## Review Lenses

### MVVM and UI

- Flag browser dashboard React components that own workflow labels, disabled reasons, live-region copy, route trust, or empty/detail state that belongs in a view model or catalog.
- Flag browser workstation regressions in keyboard selection, `aria-selected`, accessible names, hash/query deep links, and safe live-action gating.
- Flag business logic in WPF code-behind.
- Flag view code that manipulates controls directly instead of using bindings and commands.
- Flag WPF-only dependencies leaking into shared UI services.

### Performance and Concurrency

- Flag `.Result`, `.Wait()`, sync dispatch, or unbounded channel usage.
- Watch hot paths for unnecessary allocations, excessive LINQ, and logging noise.
- Check cancellation flow on loops, pipelines, and receive/read paths.

### Resilience and Error Handling

- Flag swallowed exceptions, especially cancellation.
- Flag direct `Exception` usage where project exceptions should be used.
- Check shutdown and disposal paths for flush, disconnect, and cleanup semantics.

### Storage and Serialization

- Flag direct file writes where WAL or `AtomicFileWriter` should be used.
- Flag reflection-based JSON serialization where ADR-014 contexts should be used.
- Check persistence code for flush semantics and crash-safety expectations.

### Providers and Integrations

- Check reconnection and resubscription paths for streaming providers.
- Check rate limiting and `IOptionsMonitor<T>` usage for provider config.
- Check attribute and contract compliance on provider implementations.

### Tests

- Flag missing coverage for happy path, error path, cancellation path, and disposal.
- Flag flaky timing and `async void`.
- Check whether the touched layer is validated in the right test project.
- For `src/Meridian.Ui/dashboard/`, expect targeted Vitest, TypeScript, or build validation for user-visible UI behavior.

## Output Standards

- List findings in severity order.
- Include file and line references when available.
- Say explicitly when no findings were found.
- Mention residual risks or validation gaps if tests were not run or if review scope is partial.
