# meridian-test-writer — Changelog

## v1.0.0 (2026-03-16)

### Added

- Initial skill release targeting all major Meridian component types across 4 test projects
- **Component type decision tree** — maps component to test project, subdirectory, pattern
  letter (A–H), and mock library (Moq vs NSubstitute)
- **7 universal test quality rules** with code examples for each:
  - No `async void`
  - CancellationToken with 5-second timeout
  - `await using` for `IAsyncDisposable`
  - No `Task.Delay` for synchronization
  - `MethodUnderTest_Scenario_ExpectedBehavior` naming
  - No shared static mutable state
  - Temp directory isolation for storage tests
- **8 named patterns (A–H)** covering:
  - Pattern A: Historical provider tests (HTTP mock, rate limit, cancellation, empty response)
  - Pattern B: Streaming provider tests (connect, disconnect, reconnect, dispose)
  - Pattern C: Storage sink tests (write, flush, dispose, temp dir cleanup)
  - Pattern D: Pipeline / EventPipeline tests (sink callback, flush ordering)
  - Pattern E: Application service tests (`[Theory]` / `[InlineData]`)
  - Pattern F: WPF / UI Service tests (API mock, graceful null-on-error)
  - Pattern G: F# interop tests (F# test module style, `Result` assertions)
  - Pattern H: Endpoint integration tests (WebApplicationFactory)
- **Minimum test coverage requirements** — happy path, error path, cancellation,
  boundary conditions, disposal/cleanup (plus reconnection for streaming, flush semantics for sinks)
- **Step 4 pre-submit checklist** aligned with `meridian-code-review` Lens 4
- **Quick reference tables** — FluentAssertions, Moq, and NSubstitute assertion patterns
- **Anti-patterns table** — 8 documented anti-patterns with symptoms and fixes
- **`references/test-patterns.md`** — complete scaffolding for all 8 patterns including
  full compilable code examples, `TestDataBuilder` helper, and test file placement table
