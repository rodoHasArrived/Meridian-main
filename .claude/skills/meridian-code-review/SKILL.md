---
name: meridian-code-review
description: >
  Code review and architecture compliance skill for Meridian. Use when the user asks to review,
  audit, refactor, or improve Meridian C# or F# code, or when shared files mention MVVM,
  ViewModels, code-behind cleanup, performance, pipeline throughput, provider implementations,
  backfill logic, data integrity, error handling, test quality, ProviderSdk compliance, dependency
  violations, JSON source generators, hot config reload, WPF architecture, storage sinks, WAL
  correctness, or AtomicFileWriter usage. Trigger even without naming the project if code references
  Meridian namespaces, BindableBase, EventPipeline, IMarketDataClient, IStorageSink,
  WriteAheadLog, AtomicFileWriter, or ProviderSdk types.
license: See repository LICENSE
last_updated: 2026-03-21
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads repository files, optional
  reference bundles, and Python helper scripts for evals, packaging, and deterministic validation.
metadata:
  owner: meridian-ai
  version: "1.1"
  spec: open-agent-skills-v1
---
# Meridian Code Review

**Last Updated:** 2026-03-21

> **GitHub Copilot / Actions equivalent:** [`.github/agents/code-review-agent.md`](../../../.github/agents/code-review-agent.md) — same lens framework as a GitHub agent definition.
> **Navigation index:** [`docs/ai/skills/README.md`](../../../docs/ai/skills/README.md)
> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md) — authoritative stats, file paths, provider list, ADR table.

## Integration Pattern

Every code review task follows this 4-step workflow:

### 1 — GATHER CONTEXT (MCP)
- Fetch the GitHub issue or PR to understand what changed and why
- Read the relevant `.cs` / `.fs` / `.xaml` files that are in scope
- Check `docs/ai/ai-known-errors.md` for recurring agent mistakes that apply

### 2 — ANALYZE & PLAN (Agents)
- Identify which of the 7 lenses apply to the files under review
- For multi-file reviews, map cross-file relationships before running any lens
- Plan the findings structure: which files get refactored vs. review-only output

### 3 — EXECUTE (Skills + Manual)
- Run applicable lenses and produce the review output (refactored code or categorized findings)
- Embed inline `[Mx]`/`[Px]`/`[Sx]` justification comments for every significant change
- For CRITICAL findings, include diff snippets showing before/after

### 4 — COMPLETE (MCP)
- Commit the refactored files (if in refactor mode)
- Create a PR via GitHub with a summary of findings and changes
- Request review from the appropriate team members

---

## Bundled Resources

```
meridian-code-review/
├── SKILL.md                      ← you are here
├── agents/
│   └── grader.md                 ← assertions grader for evals; read when grading test runs
├── references/
│   ├── architecture.md           ← deep project context: solution layout, dependency graph,
│   │                               pipeline design, WPF layer, provider/backfill, F# interop,
│   │                               testing conventions, ADRs — read for deep review context
│   └── schemas.md                ← JSON schemas for evals.json, grading.json, benchmark.json
├── evals/
│   ├── evals.json                ← eval set (12 test cases with assertions)
│   └── benchmark_baseline.json  ← accepted pass-rate baselines per eval (regression floor)
├── eval-viewer/
│   ├── generate_review.py        ← launch the eval review viewer
│   └── viewer.html               ← the viewer HTML
└── scripts/
    ├── aggregate_benchmark.py    ← aggregate grading results into benchmark.json
    ├── run_eval.py               ← run a single eval
    ├── package_skill.py          ← package this skill into a .skill file
    └── utils.py                  ← shared utilities
```

**When to read `references/architecture.md`**: Full solution layout, exact dependency rules, storage sink patterns, WAL guarantees, WPF MVVM patterns, backfill architecture, F# interop boundary rules, testing conventions, and ADR quick reference.

**When to read `../_shared/project-context.md`**: Current project statistics (file counts, test counts), key abstraction interfaces with file paths, provider inventory, storage organization modes, and naming conventions. This is the authoritative, always-up-to-date snapshot.

---

A unified code review skill that catches architecture violations, performance anti-patterns, error handling gaps, test quality issues, provider compliance problems, and storage/pipeline integrity issues in the Meridian codebase.

## Context: What This Project Is

Meridian is a high-throughput .NET 9 / C# 13 system (with F# 8.0 domain models) that captures real-time market microstructure data (trades, quotes, L2 order books) from multiple providers (Alpaca, Polygon, Interactive Brokers, StockSharp, NYSE) and persists it via a backpressured pipeline to JSONL/Parquet storage with WAL durability. Historical backfill from 10+ providers with automatic failover. WPF desktop app (recommended) and web dashboard share services through a layered architecture.

**Key facts for reviewers (authoritative counts in `../_shared/project-context.md`):**
- **779 source files**: 769 C#, 14 F#, 266 test files, ~4,135 test methods across 4 test projects
- **WPF is the primary desktop target.** UWP was fully removed — flag ANY `using Windows.*` or `using Meridian.Uwp.*` as CRITICAL.
- The project has strong backend patterns — bounded channels, Write-Ahead Logging, batched flushing, backpressure signals. Primary improvement area: WPF desktop layer where business logic accumulates in XAML code-behind.
- `Meridian.ProviderSdk` defines clean interfaces for provider implementations.
- F# domain models in `Meridian.FSharp` require care at C#/F# interop boundaries.
- Storage writes MUST go through `AtomicFileWriter` — never direct `FileStream`/`File.WriteAllText`.

---

## Multi-File Review Mode

When the user shares **2 or more files** together, activate multi-file review mode:

1. **Map relationships first.** Before running any lens, identify how the files relate: View + ViewModel pair? Provider + its test? Sink + its consumer? Output this map explicitly: `**File relationships:** DashboardPage.xaml.cs (View) ↔ DashboardViewModel.cs (ViewModel)`.

2. **Cross-file checks.** Run these in addition to the per-file lens checks:
   - **View/ViewModel pair**: Does the View's XAML bind to properties/commands that actually exist on the ViewModel? Does the ViewModel expose all state the View needs?
   - **Provider + test pair**: Does the test exercise the reconnection path? The rate-limiting path? The `DisposeAsync` cancellation path?
   - **Sink + consumer**: Does the consumer call `FlushAsync` after the write loop? Does it `await using` the sink?
   - **Service + interface**: Does the implementation satisfy ALL interface methods with correct signatures?

3. **Dependency check section.** Add a `## Cross-File Dependencies` section to the review output listing any cross-file contract violations found.

---

## Review Framework: 7 Lenses

Apply relevant lenses based on what the code does. Not every lens applies to every file — use judgment.

### Lens 1: MVVM Architecture Compliance
*Apply to: `.xaml.cs` files, `ViewModels/`, `Views/`*

- **CRITICAL**: Business logic in code-behind (rate calculations, data transformations, state management)
- **CRITICAL**: Service dependencies injected into `Page`/`Window` constructor instead of ViewModel
- **CRITICAL**: Direct UI element manipulation (`PublishedCount.Text = ...`)
- **WARNING**: `DispatcherTimer` in code-behind — use `PeriodicTimer` in ViewModel
- **WARNING**: `FindResource()` called on every update — cache as static field
- **WARNING**: Timer interval hardcoded — should use `IOptionsMonitor<UiSettings>` (ADR-021)
- **WARNING**: Click handlers containing business logic — convert to `RelayCommand`
- Refactored ViewModels must inherit `BindableBase`, use `SetProperty`, expose `RelayCommand` instances.

### Lens 2: Real-Time Performance
*Apply to: pipeline code, hot-path event processing, flush loops*

- **CRITICAL**: `Channel.CreateUnbounded<T>()` — must use `EventPipelinePolicy.*.CreateChannel<T>()` (ADR-013)
- **CRITICAL**: `.Wait()` / `.Result` blocking — causes deadlocks and thread pool starvation
- **CRITICAL**: `Dispatcher.Invoke` (synchronous) in event handlers — use `InvokeAsync`
- **WARNING**: `.ToList()`, `.Where()`, `.Select()` LINQ in flush loops — hot-path allocation
- **WARNING**: `LogDebug`/`LogTrace` in production hot paths — use Prometheus counters instead
- **WARNING**: `new byte[]` buffer allocations per-event — use `ArrayPool<T>` or reuse
- **WARNING**: Missing `CancellationToken` in `ReadAllAsync`/`WriteAsync` calls
- **INFO**: String interpolation in any log call — use structured logging (semantic params)

### Lens 3: Error Handling & Resilience
*Apply to: services, providers, orchestrators*

- **CRITICAL**: `catch (Exception)` that swallows `OperationCanceledException` — must re-throw or check `ct.IsCancellationRequested`
- **CRITICAL**: `throw new Exception(...)` — use project exception hierarchy from `Core/Exceptions/`
- **WARNING**: Missing `CancellationToken` parameter on async methods
- **WARNING**: `DisposeAsync` not cancelling outstanding operations before closing connections
- **WARNING**: Missing `FlushAsync` call on `IStorageSink` after write loop completes
- **WARNING**: No retry/backoff on transient provider errors
- **INFO**: Missing progress reporting on long-running operations (backfill, bulk export)

### Lens 4: Test Code Quality
*Apply to: test files (`*Tests.cs`, `*.Tests/`)*

- **CRITICAL**: `async void` test methods — xUnit won't await them; tests silently pass on any failure
- **WARNING**: Shared static mutable state between tests — violates isolation
- **WARNING**: `Task.Delay(N)` for timing synchronization — use `TaskCompletionSource` or `SemaphoreSlim`
- **WARNING**: `Assert.True(true)` or `Assert.NotNull(obj)` with no semantic meaning
- **WARNING**: `pipeline.Dispose()` instead of `await pipeline.DisposeAsync()` for `IAsyncDisposable`
- **WARNING**: Missing `using`/`await using` for `IDisposable`/`IAsyncDisposable` test subjects
- **INFO**: Test name doesn't follow `MethodUnderTest_Scenario_ExpectedBehavior` convention
- **INFO**: Missing `CancellationToken` with timeout — tests can hang indefinitely

### Lens 5: Provider Implementation Compliance
*Apply to: `Adapters/*/`, `IMarketDataClient` implementations, `IHistoricalDataProvider` implementations*

- **CRITICAL**: No reconnection logic on WebSocket disconnect
- **CRITICAL**: No rate limit handling via `ProviderRateLimitTracker`
- **WARNING**: `IOptions<T>` for settings that may change at runtime — use `IOptionsMonitor<T>`
- **WARNING**: `JsonSerializer.Serialize/Deserialize` without source-generated context (ADR-014)
- **WARNING**: `CancellationToken.None` passed to async operations — should forward the `ct` parameter
- **WARNING**: Buffer allocation per-receive — reuse or pool
- **WARNING**: Missing null-guard before `_ws!` — state check required
- **WARNING**: Missing `[ImplementsAdr]` attribute on provider class
- **INFO**: Missing `[DataSource("provider-name")]` attribute

### Lens 6: Cross-Cutting Concerns
*Apply to: all files*

- **CRITICAL**: Any `using Windows.*` or UWP APIs in platform-neutral projects (`Ui.Services`, `Ui.Shared`, `Domain`, `Application`)
- **CRITICAL**: Reverse dependency — `Ui.Services` referencing `Wpf` host types
- **CRITICAL**: `ProviderSdk` or `FSharp` importing anything except `Contracts`
- **WARNING**: F# discriminated union matching with `_ =>` catch-all — exhaustive matching required
- **WARNING**: `FSharpOption<T>` accessed via null check instead of `.IsSome`/`.Value`
- **WARNING**: F# record property assignment from C# — use `with` expressions
- **WARNING**: `<PackageReference Include="Foo" Version="1.0" />` — CPM violation (NU1008)
- **INFO**: Missing `sealed` on concrete classes not designed for inheritance

### Lens 7: Storage & Pipeline Integrity *(new)*
*Apply to: `IStorageSink` implementations, `WriteAheadLog`, storage services, pipeline flush code*

- **CRITICAL**: Writing directly to `FileStream`/`File.WriteAllText` in a sink — must use `AtomicFileWriter` (ADR-007); direct writes produce partial JSONL records on crash
- **CRITICAL**: `WriteAheadLog.FlushAsync()` not called before `DisposeAsync()` — WAL entries lost on crash
- **CRITICAL**: `IStorageSink.FlushAsync()` not called after write loop — in-flight events lost on graceful shutdown
- **CRITICAL**: `JsonSerializer.Serialize/Deserialize` without source-generated context in a storage path (ADR-014)
- **WARNING**: `IStorageSink` implementation not implementing `IFlushable` — shutdown ordering broken
- **WARNING**: WAL entry written after in-memory enqueue — write WAL first, then enqueue (crash safety requires this order)
- **WARNING**: Storage sink not registered via `[StorageSink]` attribute — will not be discovered by `StorageSinkRegistry`
- **WARNING**: Parquet conversion triggering during active writes — should only run after session close or explicit compaction trigger
- **WARNING**: Retention policy engine running without acquiring a file lock — may compact files being actively written
- **INFO**: Storage path construction not using `StorageOptions.GetPath(...)` helper — inconsistent naming across org modes
- **INFO**: Missing checksum validation on WAL replay — silent data corruption on replay

---

## Review Output Format

### Always produce both outputs

**For all requests** (refactor or review-only), produce:
1. The primary output (refactored code OR markdown findings)
2. An inline justification for each significant change (diff snippets in review-only mode; inline comments in refactor mode)

---

**For refactoring requests**, produce a complete, compilable C# file with inline finding comments:

```csharp
// =============================================================================
// REVIEW SUMMARY
// =============================================================================
// File: DashboardViewModel.cs (extracted from DashboardPage.xaml.cs)
//
// MVVM Findings:
//   [M1] Extracted business logic from code-behind to ViewModel
//   [M2] Replaced direct UI manipulation with bindable properties
//   [M3] Converted click handlers to ICommand (RelayCommand)
//   [M4] Moved timer management to ViewModel with PeriodicTimer
//
// Performance Findings:
//   [P1] Cached FindResource() brush lookups as static fields
//   [P2] Replaced Dispatcher.Invoke with InvokeAsync where possible
//   [P3] Added CancellationToken propagation to async methods
//
// Error Handling Findings:
//   [E1] Replaced bare Exception with ProviderException
//   [E2] Added reconnection logic with exponential backoff
//
// Storage & Pipeline Findings:
//   [S1] Routed file write through AtomicFileWriter — prevents partial JSONL on crash
//   [S2] Added FlushAsync call after write loop — prevents in-flight event loss
//
// Test Findings:
//   [T1] Renamed test methods to follow naming convention
//   [T2] Added CancellationToken timeout to async tests
//
// Provider/Backfill Findings:
//   [B1] Added rate limit tracking via ProviderRateLimitTracker
//   [B2] Switched from IOptions<T> to IOptionsMonitor<T> for hot reload
//
// Conventions:
//   [C1] Fixed string interpolation in log calls (semantic params)
//   [C2] Added [ImplementsAdr] attribute
//
// Breaking Changes: None — existing XAML bindings need updating to match
// new property names (see binding migration notes below).
// =============================================================================

namespace Meridian.Wpf.ViewModels;

public sealed class DashboardViewModel : BindableBase  // [M1] extracted from code-behind
{
    // ... refactored code with inline [Mx]/[Px]/[Sx] comments on changed lines
}
```

---

**For review-only requests**, produce categorized markdown findings. For each CRITICAL or WARNING finding, include a **diff snippet** showing the fix:

````markdown
## MVVM Compliance
- **[M1] CRITICAL**: Business logic in code-behind (line 42-67) — rate calculation belongs in ViewModel

  ```diff
  - private async void RefreshTimer_Tick(object? sender, EventArgs e)
  - {
  -     var rate = (status.Published - _previousPublished) / 2.0;
  -     PublishedCount.Text = FormatNumber(status.Published);
  + // In DashboardViewModel.cs:
  + public void UpdateStatus(CollectorStatus status, double elapsedSeconds)
  + {
  +     var rate = (status.Published - _previousPublished) / elapsedSeconds;
  +     PublishedCount = FormatNumber(status.Published);  // bindable property
  ```

## Storage & Pipeline Integrity
- **[S1] CRITICAL**: Writing directly to FileStream in JsonlStorageSink (line 89) — use AtomicFileWriter

  ```diff
  - await using var fs = File.OpenWrite(path);
  - await JsonSerializer.SerializeAsync(fs, evt, ctx);
  + await _atomicWriter.WriteAsync(path, stream =>
  +     JsonSerializer.SerializeAsync(stream, evt, MarketDataJsonContext.Default.MarketEvent));
  ```
````

---

Severity levels:
- **CRITICAL**: Will cause bugs, data loss, crash-safety violations, or significant performance degradation
- **WARNING**: Architectural violation or performance concern that should be addressed
- **INFO**: Style/convention deviation, minor improvement opportunity

---

## Project-Specific Conventions to Enforce

See `../_shared/project-context.md` § "Naming & Coding Conventions" for the full table. Key conventions:

- **Naming:** Async suffix, `ct`/`cancellationToken`, `_` prefix for fields, `I` prefix for interfaces
- **Logging:** Structured logging with semantic params — never string interpolation in log calls
- **Architecture:** `EventPipelinePolicy.*.CreateChannel<T>()` for channels; `AtomicFileWriter` for all sink writes
- **Serialization (ADR-014):** Source-generated `MarketDataJsonContext` — never reflection-based serialization
- **Hot config:** `IOptionsMonitor<T>` for runtime-changeable settings; `IOptions<T>` only for startup-static config
- **Desktop:** WPF only — UWP fully removed; `Ui.Shared` and `Ui.Services` must stay platform-neutral
- **F# interop:** Handle `FSharpOption<T>` properly; exhaustive pattern matching on all DU cases; no property setters on F# records
- **Benchmarks:** `[MemoryDiagnoser]`, `[GlobalSetup]`, `[Benchmark(Baseline = true)]`, zero-allocation targets
- **CPM:** No `Version=` attribute on `<PackageReference>` items (causes NU1008)
- **ADR compliance:** `[ImplementsAdr("ADR-XXX", "reason")]` on all provider and pipeline implementations

---

## Running Evals (for skill development)

To test or improve this skill using the bundled eval set:

**1. Run a test case manually** (Claude.ai — no subagents):
Read `evals/evals.json`, pick a prompt, follow this skill's instructions to produce the review output, save to a workspace dir.

**2. Grade the output**:
Read `agents/grader.md` and evaluate the assertions from `evals/evals.json` against the output. Save results to `grading.json` alongside the output.

**3. View results**:
```bash
python eval-viewer/generate_review.py \
  --workspace <path-to-workspace>/iteration-1 \
  --skill-name meridian-code-review \
  --static /tmp/mdc_review.html
```
Then open `/tmp/mdc_review.html` in a browser.

**4. Aggregate benchmark**:
```bash
python -m scripts.aggregate_benchmark <workspace>/iteration-1 --skill-name meridian-code-review
```

The aggregator will compare results against `evals/benchmark_baseline.json` and warn if any eval drops more than 10 percentage points below its accepted baseline.

**5. Package the skill** when done:
```bash
python scripts/package_skill.py /tmp/meridian-code-review
```

See `references/schemas.md` for full JSON schemas.
