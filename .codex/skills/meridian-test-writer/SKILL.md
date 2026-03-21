---
name: meridian-test-writer
description: Write or expand Meridian tests with the project's preferred patterns. Use when the user asks for tests, coverage, missing unit tests, regression tests, integration tests, or validation for Meridian providers, storage, pipelines, services, WPF view models, UI services, execution code, or F# interop.
---

# Meridian Test Writer

Write tests that fit Meridian's structure, naming, disposal rules, and async behavior without adding flaky timing or generic boilerplate.

Read `../_shared/project-context.md` before choosing the test project. Read `references/test-patterns.md` when you need quick guidance on component-to-test-project mapping.

## Workflow

1. Read the source under test and identify its contract, dependencies, and failure modes.
2. Choose the correct test project before writing any code.
3. Cover happy path, error path, cancellation path, and disposal or cleanup where relevant.
4. Use the mock library already used by the target project.
5. Run the narrowest relevant `dotnet test` command and report it.

## Universal Rules

- Use `async Task`, never `async void`.
- Use a timeout-backed `CancellationTokenSource` for async tests.
- Use `await using` with `IAsyncDisposable`.
- Do not use `Task.Delay` for synchronization unless there is no realistic alternative and the reason is stated.
- Name tests `MethodUnderTest_Scenario_ExpectedBehavior`.
- Avoid shared mutable static test state.
- Clean up temp files and directories deterministically.

## Coverage Expectations

- Happy path
- Error or exception path
- Cancellation path
- Boundary inputs
- Disposal and persistence semantics when the component owns resources

## Meridian-Specific Guidance

- Providers belong in the infrastructure/provider-oriented test areas.
- Storage, WAL, and pipeline code need stronger cleanup and flush assertions.
- WPF and shared UI services should respect the existing test project's mocking style.
- F# interop tests should focus on the boundary contract, not re-implementing the F# logic in C#.
