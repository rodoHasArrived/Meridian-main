---
name: meridian-test-writer
description: >
  Write or expand Meridian tests grounded in real-world market scenarios that exercise complete
  code paths end-to-end, rather than arbitrarily calling individual methods. Use when the user
  asks for tests, coverage, missing unit tests, regression tests, integration tests, validation
  for Meridian providers, storage, pipelines, services, browser dashboard view models/components,
  retained WPF view models, UI services, execution code, F# interop, or when they describe a market event (e.g. "flash crash", "session open",
  "feed interruption") and want to know how the system handles it.
---

# Meridian Test Writer

Write tests that simulate what the system will actually experience in production. Every test must
be grounded in a named, real-world market scenario and must exercise at least the full relevant
code path — not just a single method in isolation.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before choosing
the test project. Read
`references/test-patterns.md` for the component-to-test-project mapping and the Market Scenario
Catalog.

---

## Use When

Use this skill when the user asks for tests, coverage, regression protection, validation scenarios,
or market-event handling in Meridian.

Trigger examples:

- "Write regression tests for the paper-session replay bug."
- "Add coverage for this provider failure path."
- "How would the system handle a feed interruption? Test it."

## Do Not Use When

Use `meridian-code-review` to identify missing tests without writing them, `meridian-blueprint` for
test strategy in a design document, and `meridian-implementation-assurance` for final certification
after implementation.

Non-trigger examples:

- "Review this diff and tell me if tests are missing."
- "Design the testing approach for a future feature."
- "Certify this completed implementation."

---

## Core Philosophy: Scenario-First Testing

**Wrong:** "I need to cover `TradeDataCollector.OnTrade`. I will call it with valid inputs,
invalid inputs, and a cancelled token."

**Right:** "During a session open, a burst of sequential trades arrives. Let me feed that scenario
through the real collector, pipeline, and storage — and assert on what an operator would see."

Before writing any code, answer: *"Which named market scenario does this test simulate?"*
Use the Scenario Catalog in `references/test-patterns.md` to find a match. If none fits,
name the new scenario before proceeding.

---

## Workflow

1. Identify the **named market scenario** from the Scenario Catalog.
2. **For provider tests:** study the provider's wire format using the Provider Wire-Format Catalog in
   `references/test-patterns.md` and any existing recorded-session fixtures under
   `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/` before constructing test inputs.
3. Trace the **full code path** from the scenario trigger to the observable outcome.
4. Choose the correct test project and subdirectory.
5. Use `MarketScenarioBuilder` to build realistic event sequences (never magic-constant prices).
6. Cover happy path, error path, cancellation path, and disposal or cleanup where relevant.
7. Use the mock library already used by the target project (Moq for `Meridian.Tests`; NSubstitute for `Meridian.Ui.Tests`).
8. Run the narrowest relevant `dotnet test` command and report it.

## Handoffs

- Hand off from `meridian-code-review` when missing tests are the actionable finding.
- Hand off to `meridian-provider-builder` when tests expose missing provider implementation work.
- Hand off to `meridian-implementation-assurance` when tests are part of a broader implementation proof.

## Validation

- Run the narrowest test command for the touched project and filter where practical.
- Use dashboard-local Vitest/build commands for `src/Meridian.Ui/dashboard/` tests.
- If tests cannot run, report the exact blocker and residual risk.

---

## Universal Rules

- Use `async Task`, never `async void`.
- Use a timeout-backed `CancellationTokenSource` for async tests.
- Use `await using` with `IAsyncDisposable`.
- Do not use `Task.Delay` for synchronization unless there is no realistic alternative and the reason is stated.
- Name scenario tests `Scenario_MarketCondition_SystemBehavior`; name unit tests `MethodUnderTest_Scenario_ExpectedBehavior`.
- Avoid shared mutable static test state.
- Clean up temp files and directories deterministically.
- Add an XML `<summary>` doc comment to each scenario class naming the market failure mode it guards against.

---

## Coverage Expectations

- Happy path
- Error or exception path
- Cancellation path
- Boundary inputs
- Disposal and persistence semantics when the component owns resources
- **[Scenario tests]** Full code path (≥2 architectural layers)
- **[Scenario tests]** Assertion captures a business-observable outcome, not just "no exception"

---

## Meridian-Specific Guidance

- Providers belong in the infrastructure/provider-oriented test areas.
- **For streaming providers (Pattern B):** study the provider's official API docs and use the exact
  wire-format field names/types when constructing test inputs. See the Provider Wire-Format Catalog
  in `references/test-patterns.md`. Never invent plausible-looking JSON — field names, timestamp
  formats, and condition-code types differ significantly between providers (e.g., Alpaca uses
  nanosecond ISO 8601 and string exchange codes; Polygon uses millisecond epoch integers).
- **For historical providers (Pattern A):** check the recorded-session fixtures in
  `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/` and the official provider docs in the
  Provider Wire-Format Catalog before constructing mock HTTP responses.
- Storage, WAL, and pipeline code need stronger cleanup and flush assertions.
- Browser workstation tests should use the existing Vitest and Testing Library patterns under `src/Meridian.Ui/dashboard/`.
- Retained WPF and shared UI services should respect the existing test project's mocking style.
- F# interop tests should focus on the boundary contract, not re-implementing the F# logic in C#.
- Multi-layer scenario tests belong in `tests/Meridian.Tests/Integration/`.
- Use `MarketScenarioBuilder` from `tests/Meridian.Tests/TestHelpers/MarketScenarioBuilder.cs`
  instead of hand-crafting events with arbitrary prices.

---

## Pattern Quick Reference

| Component | Pattern | Key Concerns |
|-----------|---------|-------------|
| `IHistoricalDataProvider` | A | HTTP errors, rate limit, cancellation, empty |
| `IMarketDataClient` | B | Connect/disconnect, reconnect, dispose |
| `IStorageSink` / WAL | C | Temp dir, FlushAsync, DisposeAsync, line count |
| `EventPipeline` | D | FlushAsync before assert, DisposeAsync flushes |
| Application service (pure) | E | `[Theory]` for multiple inputs, `[InlineData]` |
| Browser dashboard | F1 | Vitest, view-model state, accessible labels, keyboard paths |
| WPF / Ui.Services | F | API mock, null on error |
| F# modules | G | F# test module style, `Result` type assertions |
| Endpoint integration | H | `WebApplicationFactory`, JSON contract snapshots |
| **Market scenario (multi-layer)** | **I** | **Named scenario, ≥2 layers, `MarketScenarioBuilder`** |

Full scaffolding for all patterns and the Market Scenario Catalog are in `references/test-patterns.md`.

## Output Standards

- Name the market or operator scenario each test protects.
- State the full code path exercised and the business-observable outcome asserted.
- Summarize tests added or changed, exact validation commands, and any remaining untested risk.
