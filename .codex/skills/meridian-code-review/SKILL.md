---
name: meridian-code-review
description: Review Meridian code for bugs, regressions, architecture drift, missing tests, and project-rule violations. Use when the user asks for a review, audit, refactor assessment, compliance check, bug-risk scan, or improvement pass on Meridian C#, F#, XAML, provider, storage, pipeline, execution, ledger, or WPF code.
---

# Meridian Code Review

Review Meridian changes with a bug-finding mindset first. Prefer findings over summary, and ground every finding in Meridian's architecture rules.

Read `../_shared/project-context.md` before reviewing. If the change touches a highly specialized area, inspect the relevant source files and tests before drawing conclusions.

## Review Order

1. Understand what changed and why.
2. Map cross-file relationships before evaluating individual lines.
3. Prioritize correctness, regressions, and contract violations.
4. Check for missing or weak tests after code-level risks.
5. Keep summaries brief; findings come first.

## Review Lenses

### MVVM and UI

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

## Output Standards

- List findings in severity order.
- Include file and line references when available.
- Say explicitly when no findings were found.
- Mention residual risks or validation gaps if tests were not run or if review scope is partial.
