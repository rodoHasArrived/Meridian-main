# meridian-test-writer — Changelog

## v1.2.0 (2026-04-08)

### Changed — Scenario-First Testing Philosophy

- **Renamed and reframed core philosophy** from "generate tests for methods" to
  "simulate real-world market scenarios that run through all aspects of the code."
- Updated skill `description` to call out scenario-grounded testing explicitly.
- Added **Scenario-First Rules** section: name the market event first, trace the full
  code path, use realistic data, encode the observable outcome, add regression notes.
- Added **Pattern I: Market Scenario Simulation** — a new pattern for multi-layer,
  scenario-driven tests that exercise ≥2 architectural layers end-to-end.
- Updated **Step 0 decision table** to include Pattern I (Market scenario) with key
  concerns: named scenario, ≥2 layers, realistic data.
- Updated **Step 1 naming convention** — Pattern I tests use
  `Scenario_MarketCondition_SystemBehavior` instead of `MethodUnderTest_Scenario_ExpectedBehavior`.
- Updated **Minimum Test Coverage Requirements** — Pattern I now requires: full code
  path (≥2 layers), realistic data via `MarketScenarioBuilder`, business-observable assertion.
- Updated **Step 4 checklist** with three new Pattern I checkboxes.
- Extended **Anti-Patterns to Avoid** table with three new entries: Arbitrary method
  calling, Magic-constant prices, Single-layer tests for cross-cutting behaviour.

### Added

- **Market Scenario Catalog** in `references/test-patterns.md` — 16 named real-world
  scenarios across 4 tiers (Data Ingestion, Backtesting, Execution & Risk, Storage &
  Recovery), each specifying which code paths must be exercised end-to-end.
- **Pattern I scaffold** in `references/test-patterns.md` — full compilable
  `NormalSessionOpenScenarioTests` example demonstrating the scenario-simulation pattern.
- **`MarketScenarioBuilder` helper** in `references/test-patterns.md` — a static factory
  class (placed at `tests/Meridian.Tests/TestHelpers/MarketScenarioBuilder.cs`) with
  `BuildSessionOpen`, `BuildSequentialTrades`, `BuildSequentialQuotes`, `BuildFlashCrash`,
  and `BuildFeedInterruption` methods that produce deterministic, realistic event sequences.
- Updated **Test File Placement** table with a Pattern I row pointing to `Integration/`.

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
