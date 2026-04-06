# Backtest Studio Unification Blueprint

**Owner:** Core Team
**Audience:** Product, Research, Architecture, Backtesting, API, Web, and WPF contributors
**Last Updated:** 2026-04-01
**Status:** Active blueprint

---

## Summary

This blueprint turns Wave 4 from a roadmap theme into a concrete implementation plan for making Meridian backtesting feel like one product regardless of engine.

The central design decision is:

- keep `StrategyRunEntry` and the workstation `StrategyRun*` contracts as the cross-workflow seam
- keep `Meridian.Backtesting.Sdk.BacktestResult` as the canonical persisted research result
- normalize Lean output into that canonical result instead of building a second research read model

That direction fits the repository's current shape:

- native runs already produce a rich [`BacktestResult`](../../src/Meridian.Backtesting.Sdk/BacktestResult.cs)
- strategy-run persistence already stores that result on [`StrategyRunEntry`](../../src/Meridian.Strategies/Models/StrategyRunEntry.cs)
- workstation comparison and diff already flow through [`StrategyRunReadService`](../../src/Meridian.Strategies/Services/StrategyRunReadService.cs) and [`WorkstationEndpoints.cs`](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs)
- Lean integration currently exposes a separate UI-facing result type in [`LeanIntegrationService.cs`](../../src/Meridian.Ui.Services/Services/LeanIntegrationService.cs) and lightweight launcher-style endpoints in [`LeanEndpoints.cs`](../../src/Meridian.Ui.Shared/Endpoints/LeanEndpoints.cs)

Wave 4 should therefore finish the unification by making engine choice an execution detail inside one Backtest Studio workflow.

---

## Scope

### In scope

- Normalize Meridian Native and Lean backtest output into one persisted result model.
- Extend shared run comparison and diff tooling for research-oriented analysis.
- Broaden fill-model realism where it materially changes research decisions.
- Improve native-engine performance for larger historical windows where operator experience clearly benefits.
- Add capability or completeness signals so engine-specific gaps are visible instead of hidden.
- Keep the resulting contracts usable from WPF, web, and promotion workflows.

### Out of scope

- Rewriting the native backtest engine core loop without a measured operator benefit.
- Replacing Lean integration with a brand-new runtime model.
- Building live-trading promotion logic beyond the existing run and promotion contracts.
- Chasing perfect engine parity when Lean does not expose equivalent artifacts.
- Deep QuantScript or L3 simulation work that belongs to other tracks.

### Assumptions

- `StrategyRunEntry` remains the durable workflow object across Research, Trading, and Governance.
- `BacktestResult` remains the canonical engine-neutral payload unless a proven gap requires additive fields.
- Research surfaces should show missing fidelity explicitly rather than synthesizing data that did not exist in the source engine output.

---

## Architecture

### 1. Canonical result path

Use the existing `Meridian.Backtesting.Sdk.BacktestResult` as the research-system contract for all completed backtests.

Current state:

- native runs already emit `BacktestResult` with request, universe, snapshots, cash flows, fills, metrics, ledger, elapsed time, event count, trade tickets, and optional TCA
- shared run storage already persists `BacktestResult` on `StrategyRunEntry.Metrics`
- workstation read models already derive browser rows, detail views, fills, attribution, and comparison from persisted strategy runs

Target state:

- every completed Lean run is converted into a `BacktestResult` before it enters strategy-run storage
- `StrategyRunEntry.Engine` is the engine discriminator
- `StrategyRunDetail` stays the top-level workflow detail, while research-specific drill-ins are additive and engine-neutral

### 2. Engine adapters, not engine-specific read models

Introduce an application-level orchestration seam above the raw engines:

```csharp
namespace Meridian.Application.Backtesting;

public interface IBacktestStudioEngine
{
    string EngineId { get; }
    Task<BacktestStudioRunHandle> StartAsync(BacktestStudioRunRequest request, CancellationToken ct);
    Task<BacktestStudioRunStatus> GetStatusAsync(string runHandle, CancellationToken ct);
    Task<BacktestResult> GetCanonicalResultAsync(string runHandle, CancellationToken ct);
}
```

Concrete implementations:

- `MeridianNativeBacktestStudioEngine`
- `LeanBacktestStudioEngine`

The native implementation can wrap the current `Meridian.Backtesting` path directly. The Lean implementation should wrap the existing Lean launcher plus result-ingest flow and return a canonical `BacktestResult`.

### 3. Result normalization and completeness metadata

Wave 4 needs additive metadata on top of `BacktestResult` so comparison tooling can distinguish:

- fully comparable metrics
- partially comparable metrics
- engine-specific diagnostics
- missing artifacts

Additive SDK types:

```csharp
namespace Meridian.Backtesting.Sdk;

public enum BacktestArtifactStatus : byte
{
    Missing,
    Partial,
    Complete
}

public sealed record BacktestArtifactCoverage(
    BacktestArtifactStatus Snapshots,
    BacktestArtifactStatus CashFlows,
    BacktestArtifactStatus Fills,
    BacktestArtifactStatus TradeTickets,
    BacktestArtifactStatus Ledger,
    BacktestArtifactStatus TcaReport);

public sealed record BacktestEngineMetadata(
    string EngineId,
    string EngineVersion,
    string SourceFormat,
    IReadOnlyDictionary<string, string> Diagnostics);
```

Then extend `BacktestResult` additively with:

- `BacktestArtifactCoverage Coverage`
- `BacktestEngineMetadata EngineMetadata`

This lets the product say "Lean result imported successfully, fill tape partial, ledger reconstructed from summary-only data" instead of pretending all engines produced identical evidence.

### 4. Shared run model stays primary

Do not replace the workstation contracts in [`StrategyRunReadModels.cs`](../../src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs). Instead, extend them with research-facing additions that preserve the existing cross-workflow object model.

Recommended additions:

```csharp
public sealed record StrategyRunResearchSummary(
    string RunId,
    StrategyRunEngine Engine,
    BacktestArtifactCoverage Coverage,
    string? BenchmarkSymbol,
    string? ScenarioName,
    string? FillModelProfile);

public sealed record StrategyRunMetricDelta(
    string MetricName,
    decimal? BaseValue,
    decimal? TargetValue,
    decimal? Delta,
    string? Notes = null);

public sealed record StrategyRunResearchDiff(
    string BaseRunId,
    string TargetRunId,
    IReadOnlyList<StrategyRunMetricDelta> Metrics,
    IReadOnlyList<SymbolAttributionEntry> AttributionDeltaLeaders,
    IReadOnlyList<RunFillEntry> FillDrift,
    IReadOnlyList<string> CoverageWarnings);
```

Use `StrategyRunDetail` as the umbrella object and make research drill-ins fetchable as adjacent endpoints instead of embedding every heavy artifact in the browser payload.

### 5. Comparison and diff should build on existing workstation seams

[`WorkstationEndpoints.cs`](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs) already exposes `/runs/compare` and `/runs/diff`, but the current diff is intentionally shallow.

Wave 4 should extend the current `StrategyRunReadService` path rather than invent a second comparison system.

Recommended service split:

- keep `StrategyRunReadService` responsible for summary and detail assembly
- add `StrategyRunComparisonService` for heavier research comparisons and normalization rules

```csharp
namespace Meridian.Strategies.Services;

public interface IStrategyRunComparisonService
{
    Task<IReadOnlyList<StrategyRunComparison>> CompareAsync(IEnumerable<string> runIds, CancellationToken ct);
    Task<StrategyRunResearchDiff?> DiffAsync(string baseRunId, string targetRunId, CancellationToken ct);
}
```

This keeps `StrategyRunReadService` from becoming an everything-service while preserving the existing workstation API shape.

### 6. Fill-model realism should use explicit profiles

The native engine already has:

- `OrderBookFillModel`
- `BarMidpointFillModel`
- `MarketImpactFillModel`

in [`src/Meridian.Backtesting/FillModels/`](../../src/Meridian.Backtesting/FillModels).

Wave 4 should not add more booleans to `BacktestRequest`. Instead, introduce named fill profiles:

```csharp
public enum BacktestFillProfile : byte
{
    FastApproximate,
    BarRealistic,
    OrderBookAware,
    MarketImpactStress
}
```

Then add a resolver:

```csharp
public interface IFillModelProfileResolver
{
    FillModelProfile Resolve(BacktestRequest request);
}
```

Profile intent:

- `FastApproximate`: current lightweight baseline for quick iteration
- `BarRealistic`: midpoint plus spread/participation realism for most research
- `OrderBookAware`: prefer L2-sensitive fills when order-book events exist
- `MarketImpactStress`: pessimistic fills for capacity and robustness testing

This is a cleaner migration path than letting per-order flags become the user-facing product vocabulary.

### 7. Performance work should be selective and evidence-based

Operator-facing performance improvements belong where the UI or run queue visibly suffers, not as a general rewrite.

Wave 4 performance focus:

- reduce repeated materialization of large snapshot and fill collections during comparison and diff
- add summary-side caching for expensive derived metrics in `StrategyRunReadService` or the new comparison service
- stream or window large historical reads where the native engine or result readers currently load too much at once
- avoid rebuilding ledger or attribution views unless the caller requested those artifacts

Non-goal:

- speculative micro-optimization of hot paths that do not change wall-clock experience for larger research windows

---

## Interfaces and Models

### New application-layer requests

```csharp
namespace Meridian.Application.Backtesting;

public sealed record BacktestStudioRunRequest(
    string StrategyId,
    string StrategyName,
    StrategyRunEngine Engine,
    string? DatasetReference,
    string? BenchmarkSymbol,
    IReadOnlyDictionary<string, string> Parameters,
    BacktestFillProfile FillProfile,
    BacktestRequest NativeRequest,
    IReadOnlyDictionary<string, string>? ExternalEngineOptions = null);

public sealed record BacktestStudioRunHandle(
    string RunId,
    string EngineRunHandle,
    StrategyRunEngine Engine);

public sealed record BacktestStudioRunStatus(
    string RunId,
    StrategyRunStatus Status,
    double Progress,
    DateTimeOffset StartedAt,
    DateTimeOffset? EstimatedCompletionAt,
    string? Message = null);
```

### New Lean normalization seam

```csharp
namespace Meridian.Application.Backtesting;

public interface ILeanBacktestResultNormalizer
{
    Task<BacktestResult> NormalizeAsync(
        LeanImportedBacktestArtifacts artifacts,
        BacktestStudioRunRequest request,
        CancellationToken ct);
}
```

`LeanImportedBacktestArtifacts` should collect raw Lean output before normalization, for example:

- summary stats
- equity curve
- orders or trades
- parameter set
- algorithm metadata
- artifact file paths

That keeps the translation layer explicit and testable.

### Additions to workstation contracts

Extend [`StrategyRunReadModels.cs`](../../src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs) with additive research models only:

- `StrategyRunResearchSummary`
- `StrategyRunMetricDelta`
- `StrategyRunResearchDiff`
- `BenchmarkPoint`
- `BenchmarkComparisonSummary`

Avoid creating an entirely separate `LeanBacktestResultDto` in workstation contracts.

### Endpoint direction

Keep the existing `/api/workstation/runs/*` direction and add research-oriented routes beneath it:

- `GET /api/workstation/runs/{runId}/research`
- `POST /api/workstation/runs/compare/research`
- `POST /api/workstation/runs/diff/research`
- `GET /api/workstation/runs/{runId}/benchmark`

Lean-specific transport endpoints may still exist for setup, verification, and raw ingestion, but completed runs should be accessed through workstation run routes once normalized.

---

## Data Flow

### Native engine path

1. Research UI or WPF Backtest Studio submits a `BacktestStudioRunRequest`.
2. `BacktestStudioRunOrchestrator` creates a `StrategyRunEntry` with `Engine = MeridianNative`.
3. `MeridianNativeBacktestStudioEngine` runs the current native backtest engine.
4. The engine returns canonical `BacktestResult`.
5. The orchestrator completes the run entry with that result.
6. `StrategyRunReadService` and `StrategyRunComparisonService` project shared read models for Research and downstream workflows.

### Lean path

1. Research UI or WPF Backtest Studio submits the same `BacktestStudioRunRequest`, but with `Engine = Lean`.
2. `BacktestStudioRunOrchestrator` creates a `StrategyRunEntry` with `Engine = Lean`.
3. `LeanBacktestStudioEngine` uses the existing Lean launch/status/export endpoints.
4. After Lean completes, `LeanBacktestStudioEngine` collects raw artifacts from the result location or ingest endpoint.
5. `ILeanBacktestResultNormalizer` converts those artifacts into canonical `BacktestResult` plus coverage and engine metadata.
6. The orchestrator completes the same `StrategyRunEntry` shape used by native runs.
7. Workstation and WPF read paths consume the result with no engine-specific query fork.

### Comparison and diff path

1. Research requests compare or diff through workstation routes.
2. `StrategyRunComparisonService` loads selected `StrategyRunEntry` records.
3. The service applies normalization rules:
   - compare only metrics whose coverage is compatible
   - include warnings for partial artifacts
   - compute attribution deltas, fill drift, and benchmark-relative changes when available
4. The service returns additive research diff payloads for UI rendering.

---

## Edge Cases and Risks

### Lean artifact incompleteness

Risk:

- Lean may not always provide the same fill, cash-flow, or ledger depth as native runs.

Response:

- never fabricate parity
- expose `BacktestArtifactCoverage`
- degrade comparison gracefully and show warnings in Research surfaces

### Ledger reconstruction drift

Risk:

- reconstructing a full ledger from summary-only Lean exports could create misleading audit detail.

Response:

- only build ledger views from imported artifacts when the source evidence supports it
- otherwise mark ledger coverage as `Missing` or `Partial`
- do not treat reconstructed summary views as governance-grade audit detail

### Comparison false precision

Risk:

- comparing two engines with different fill assumptions can imply a precision the data does not justify.

Response:

- include engine and fill-profile labels in comparison payloads
- show coverage warnings when result fidelity differs materially
- add benchmark and attribution comparisons as separate sections, not a single total-score rollup

### Performance regressions from heavier diffs

Risk:

- richer run diff may accidentally pull full snapshots, fills, ledger, and attribution for every compare request.

Response:

- keep browser payloads summary-only
- lazy-load heavy research diffs
- cache derived summaries per run when safe

### Contract churn

Risk:

- changing `BacktestResult` too aggressively could ripple into promotions, workstation reads, and tests.

Response:

- prefer additive fields
- keep existing constructor usage valid where possible
- migrate in two steps: SDK additions first, orchestrator/read-service adoption second

---

## Test Plan

### Unit tests

- `tests/Meridian.Backtesting.Tests/`
  - fill-profile resolver selection
  - native result coverage metadata
  - performance-sensitive comparison helpers over large snapshot/fill sets
- `tests/Meridian.Tests/`
  - Lean normalization from representative artifact fixtures into canonical `BacktestResult`
  - `StrategyRunComparisonService` compare and diff behavior across native/native, native/Lean, and Lean/Lean pairs
  - coverage-warning behavior when artifacts are missing or partial

### Contract tests

- verify `StrategyRunEntry` persistence remains backward-compatible for existing native runs
- verify workstation endpoints serialize new additive research models cleanly
- verify promotion logic still reads `BacktestResult` without engine-specific branching failures

### Integration tests

- end-to-end native run persisted and visible in shared run browser
- Lean result ingest persisted as `Engine = Lean` and visible through the same run browser
- compare and diff endpoints return compatible payloads for mixed-engine runs

### Performance validation

- compare wall-clock and allocation behavior for:
  - native backtests over larger historical windows
  - multi-run comparison over larger snapshot and fill sets
  - run-detail loading with and without heavy research drill-ins

Use targeted benchmarks only where the result changes operator wait time or UI responsiveness.

---

## Rollout

### Slice 1: Canonical result normalization

- add additive `BacktestResult` metadata
- add Lean normalization service and fixtures
- persist Lean runs as canonical `StrategyRunEntry` records

### Slice 2: Comparison and diff upgrade

- introduce `StrategyRunComparisonService`
- extend workstation research compare and diff contracts
- add coverage warnings, benchmark delta, attribution delta, and fill drift

### Slice 3: Fill-model profiles

- introduce explicit fill profiles
- map current native fill models into profile-based selection
- expose profile identity in run summaries and comparisons

### Slice 4: Measured performance pass

- optimize only the native-engine and read-model paths proven to hurt larger-window operator experience

---

## Open Questions

- Which Lean export artifact set should be treated as the minimum supported import contract for Wave 4: summary plus equity only, or summary plus equity plus trade log?
- Should benchmark comparison be stored in `BacktestResult` directly, or derived lazily from snapshots and dataset reference?
- Do we want `StrategyRunReadService` to expose research drill-ins directly, or should all heavier compare and diff logic move to a dedicated comparison service immediately?
- Is the first Backtest Studio shell expected in WPF first, web first, or both in parallel once the canonical run path is ready?
