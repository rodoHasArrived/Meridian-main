---
name: meridian-test-writer
description: >
  Test generation skill for the Meridian project. Use this skill whenever an agent
  needs to write new xUnit tests, expand coverage for existing components, or validate that
  test quality meets the project's standards. Triggers on: "write tests for", "add unit tests",
  "increase test coverage", "write a test for this class", "how do I test X", "the tests are
  missing for", "simulate market scenario", "how would the system handle", or when reviewing code
  that lacks corresponding test coverage. Also triggers when a code review (meridian-code-review)
  has identified test gaps. This skill produces idiomatic xUnit + FluentAssertions tests grounded
  in real-world market scenarios that exercise complete code paths — from provider ingestion
  through pipeline, storage, backtesting, and execution — rather than just arbitrarily exercising
  individual methods.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads repository files plus
  the bundled test pattern reference; no special runtime beyond standard markdown/resource loading.
metadata:
  owner: meridian-ai
  version: "1.2"
  spec: open-agent-skills-v1
---
# Meridian — Test Writer Skill

Generate high-quality, idiomatic xUnit tests for any Meridian component, anchored in real-world
market scenarios. Every test produced by this skill must pass the `meridian-code-review` Lens 4
(Test Code Quality) checks without warnings.

> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md)
> **Test patterns reference:** [`references/test-patterns.md`](references/test-patterns.md)
> **Code review skill:** [`../meridian-code-review/SKILL.md`](../meridian-code-review/SKILL.md)

---

## Core Philosophy: Scenario-First Testing

**Tests must simulate what the system will actually experience in production, not just call
individual methods.**

**Wrong approach:** "I need to cover `TradeDataCollector.OnTrade`. I will call it with valid
inputs, invalid inputs, and a cancelled token."

**Right approach:** "During a session open, a burst of sequential trades arrives with aggressive
buy-side imbalance. Let me write a test that feeds that scenario through the real collector,
pipeline, and storage — and assert on what an operator would see in the dashboard."

### Scenario-First Rules

1. **Name the market event first** — identify a specific, named real-world market phenomenon
   before writing a single line of code (see `references/test-patterns.md` Scenario Catalog).
2. **Trace the full code path** — every Pattern I scenario test must exercise at least two
   architectural layers end-to-end.
3. **Use realistic data shapes** — use `MarketScenarioBuilder` with plausible prices, volumes,
   tick sizes, and timestamps. Avoid magic constants like `price = 1m` unless testing a boundary.
4. **Encode the observable outcome** — the assertion must capture what an operator would see, not
   just "the method returned without throwing."
5. **Add regression notes** — XML doc `<summary>` on each scenario class naming the market failure
   mode the test guards against.

---

## Integration Pattern

Every test-writing task follows this 4-step workflow:

### 1 — GATHER CONTEXT (MCP)
- Fetch the GitHub issue, PR, or code review output that identified the test gap
- Read the source file(s) under test to understand the component's contract and dependencies
- **Identify the market scenario** the code must correctly handle (see Scenario Catalog)
- Check the target test project's `.csproj` to confirm which mock library is in use (Moq vs. NSubstitute)

### 2 — ANALYZE & PLAN (Agents)
- Detect the component type using the Step 0 decision tree
- Select the correct pattern (A–I) and target test project
- **For Pattern I:** trace the complete code path from the scenario trigger to the observable outcome
- Plan the minimum required test cases: happy path, error path, cancellation path, boundary, disposal

### 3 — EXECUTE (Skills + Manual)
- Apply all 7 universal quality rules and the selected pattern
- Write the complete, compilable test file with `CreateSut()`, proper `await using`, and timeout tokens
- **For Pattern I:** use `MarketScenarioBuilder` and include realistic market data values
- Run through the Lens 4 validation checklist before finalizing

### 4 — COMPLETE (MCP)
- Commit the test file to the appropriate test project subdirectory
- Create a PR via GitHub referencing the issue or code review finding that prompted the tests
- Request review; confirm the new tests pass in CI before marking complete

---

## Test Framework Stack

| Tool | Purpose |
|------|---------|
| **xUnit** | Test runner — all test projects |
| **FluentAssertions** | Assertion library — preferred over `Assert.*` |
| **Moq** | Mocking — `Meridian.Tests`, `Meridian.Wpf.Tests` |
| **NSubstitute** | Mocking — `Meridian.Ui.Tests` (check `.csproj` first) |
| **coverlet** | Code coverage — collected via `dotnet test --collect:"XPlat Code Coverage"` |

Always check the target test project's `.csproj` for the mock library in use before writing mocks.

---

## Step 0: Component Type Detection

Before writing any code, identify the component type using the decision tree in
`references/test-patterns.md`. The component type determines:

1. Which test project to target
2. Which subdirectory to use
3. Which pattern (A–I) to follow
4. Whether to use Moq or NSubstitute
5. Whether `IDisposable` / `IAsyncDisposable` cleanup is needed

| Component | Pattern | Key Concerns |
|-----------|---------|-------------|
| `IHistoricalDataProvider` impl | A | HTTP errors, rate limit, cancellation, empty |
| `IMarketDataClient` impl | B | Connect/disconnect, reconnect, dispose |
| `IStorageSink` / WAL | C | Temp dir, FlushAsync, DisposeAsync, line count |
| `EventPipeline` | D | FlushAsync before assert, DisposeAsync flushes |
| Application service (pure) | E | `[Theory]` for multiple inputs, `[InlineData]` |
| WPF / Ui.Services | F | API mock (Moq or NSubstitute), null on error |
| F# modules | G | F# test module style, `Result` type assertions |
| Endpoint integration | H | `WebApplicationFactory`, JSON contract snapshots |
| **Market scenario (multi-layer)** | **I** | **Named scenario, ≥2 layers, realistic data** |

**When to use Pattern I:** Prefer Pattern I whenever the behaviour under test is driven by a
recognisable market event. If you can describe what you are testing as "during a [scenario name],
the system should [observable outcome]", use Pattern I.

---

## Step 0.5: Study Provider API Documentation (Required for Provider Tests)

Before writing any test that exercises a streaming or historical provider's parsing path, complete
this checklist:

1. **Locate the recorded-session fixture** in
   `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/` for that provider (if one exists).
2. **Read the provider source file** in `src/Meridian.Infrastructure/Adapters/` to identify all
   `JsonPropertyName` annotations, DTOs, and parsing logic.
3. **Cross-reference the official docs** (see Provider Wire-Format Catalog in
   `references/test-patterns.md`) for canonical field names, timestamp formats, condition-code
   enumerations, and exchange codes.
4. **Construct wire-format messages** using the exact field names and formats found in steps 2–3 —
   never invent plausible-looking JSON.

Feeding a provider parser with authentic wire-format data catches real bugs such as timestamp
format mismatches, integer vs. string exchange codes, and off-by-one epoch conversions that hand-
crafted magic-constant JSON will silently miss.

---

## Step 1: Apply Universal Quality Rules

These 7 rules apply to **every** test, regardless of component type:

1. **Never `async void`** — always `async Task`
2. **CancellationToken with timeout** — `new CancellationTokenSource(TimeSpan.FromSeconds(5))`
3. **`await using` for `IAsyncDisposable`** — never `using` for async-disposable types
4. **No `Task.Delay` for synchronization** — use `TaskCompletionSource` or `SemaphoreSlim`
5. **Naming: `MethodUnderTest_Scenario_ExpectedBehavior`** (Pattern A–H) or
   `Scenario_MarketCondition_SystemBehavior` (Pattern I)
6. **No shared static mutable state** — each test method creates its own SUT
7. **File isolation for storage tests** — temp directory, `Dispose()` cleans it up

---

## Step 2: Select the Right Pattern

Full scaffolding for each pattern is in `references/test-patterns.md`.

---

## Step 3: Write the Test File

### Minimum Test Coverage Requirements

For any non-trivial component, a test file must cover at minimum:

- **Happy path** — valid input returns expected output
- **Error path** — invalid input or downstream failure throws correct exception type
- **Cancellation path** — `OperationCanceledException` propagates when token is cancelled
- **Boundary conditions** — null/empty/whitespace input where relevant
- **Disposal/cleanup** — `DisposeAsync` or `Dispose` completes without hanging

For storage sinks additionally:
- **Flush semantics** — data written without explicit flush is still persisted after `DisposeAsync`

For streaming providers additionally:
- **Reconnection** — a disconnect triggers reconnect, not silent data loss

For market scenario tests (Pattern I) additionally:
- **Full code path** — at least two architectural layers exercised
- **Realistic data** — `MarketScenarioBuilder` with plausible market values
- **Observable outcome** — assertion captures the business-level result

### Test Output Format

Produce a complete, compilable test file with:

1. Namespace matching the project convention (`Meridian.Tests.{Category}`)
2. `using` directives (xUnit, FluentAssertions, Moq or NSubstitute, and types under test)
3. A `CreateSut()` factory method (not scattered construction in each test method)
4. `IDisposable` or `IAsyncDisposable` implementation when temp resources are needed
5. All test methods returning `Task` (never `void`)
6. CancellationToken with 5-second timeout on every async test

---

## Step 4: Validate Before Submitting

Run through the `meridian-code-review` Lens 4 checklist mentally before finalizing:

- [ ] No `async void` test methods
- [ ] No shared static mutable state
- [ ] No `Task.Delay` for timing
- [ ] All names follow `MethodUnderTest_Scenario_ExpectedBehavior` or `Scenario_MarketCondition_SystemBehavior`
- [ ] Every `IAsyncDisposable` test subject uses `await using`
- [ ] Every async test has a `CancellationToken` with a timeout
- [ ] Storage tests clean up temp directories in `Dispose()`
- [ ] At least one test for the cancellation path
- [ ] At least one test for the error/exception path
- [ ] **[Pattern I only]** XML doc `<summary>` names the scenario and layers exercised
- [ ] **[Pattern I only]** `MarketScenarioBuilder` used with realistic prices and volumes
- [ ] **[Pattern I only]** Assertion captures a business-observable outcome

---

## Quick Reference: Common Assertions

```csharp
// Value equality
result.Should().Be(expected);
result.Should().BeEquivalentTo(expectedObject);

// Collection
items.Should().HaveCount(3);
items.Should().ContainSingle(x => x.Symbol == "AAPL");
items.Should().BeEmpty();
items.Should().NotBeEmpty();
items.Should().NotContain(x => x.Price < 0);

// Strings
str.Should().Be("expected");
str.Should().Contain("substring");
str.Should().StartWith("prefix");
str.Should().NotBeNullOrWhiteSpace();

// Exceptions
var act = () => sut.Method(input);
await act.Should().ThrowAsync<DataProviderException>();
await act.Should().ThrowAsync<DataProviderException>().WithMessage("*symbol*");
await act.Should().NotThrowAsync();

// Null
result.Should().NotBeNull();
result.Should().BeNull();

// Boolean
condition.Should().BeTrue("because ...");
condition.Should().BeFalse();

// Verify mock was called
mock.Verify(m => m.Method(It.IsAny<string>()), Times.Once);
mock.Verify(m => m.Method(It.IsAny<string>()), Times.Never);
```

---

## Quick Reference: Common Mock Setups (Moq)

```csharp
// Setup return value
mock.Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(expectedValue);

// Setup exception
mock.Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
    .ThrowsAsync(new HttpRequestException("error"));

// Setup callback
mock.Setup(m => m.WriteAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
    .Callback<MarketEvent, CancellationToken>((evt, _) => captured.Add(evt))
    .Returns(ValueTask.CompletedTask);

// Setup property
mock.Setup(m => m.CurrentValue).Returns(new MyOptions { ... });

// Verify
mock.Verify(m => m.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
mock.VerifyNoOtherCalls();  // ensure no unexpected calls
```

---

## Quick Reference: Common Mock Setups (NSubstitute)

```csharp
// Setup return value
sub.GetAsync(Arg.Any<CancellationToken>()).Returns(expectedValue);

// Setup exception
sub.GetAsync(Arg.Any<CancellationToken>()).Throws(new HttpRequestException("error"));

// Verify
sub.Received(1).FlushAsync(Arg.Any<CancellationToken>());
sub.DidNotReceive().FlushAsync(Arg.Any<CancellationToken>());
```

---

## Common Testing Anti-Patterns to Avoid

| Anti-Pattern | Symptom | Fix |
|-------------|---------|-----|
| `async void` test | Exceptions silently swallowed; test passes on failure | Change to `async Task` |
| `Task.Delay(200)` for synchronization | Flaky tests; CI timing sensitivity | Use `TaskCompletionSource` |
| No CancellationToken timeout | Test hangs indefinitely if SUT blocks | Add `new CancellationTokenSource(TimeSpan.FromSeconds(5))` |
| `Assert.True(result != null)` | Meaningless assertion, no context on failure | `result.Should().NotBeNull()` |
| Shared static `_sut` field | Test isolation violated; state leaks | Create SUT in `CreateSut()` per-test |
| `using var sink = new JsonlStorageSink(...)` | File handles leaked | `await using var sink = ...` |
| No temp dir cleanup | CI disk fills up; cross-test pollution | Implement `IDisposable` with `Directory.Delete` |
| Copy-paste test names | `Test1`, `Test2` — unintelligible | Follow `Method_Scenario_Expected` convention |
| **Arbitrary method calling** | Tests pass but do not validate real system behaviour | Ground every test in a named market scenario |
| **Magic-constant prices** | `price = 1m` reveals no intent; misses realistic edge cases | Use `MarketScenarioBuilder` with realistic values |
| **Single-layer tests for cross-cutting behaviour** | Provider test mocks HTTP but skips pipeline; misses integration regressions | Use Pattern I for cross-layer scenarios |
