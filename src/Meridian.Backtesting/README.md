---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-BACKTESTING
path: src/Meridian.Backtesting
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-07-12
---

# src/Meridian.Backtesting

## Purpose

Backtesting contains runtime support for historical strategy simulation and replay-oriented research workflows.

## Layer responsibility

This layer should keep simulation behavior isolated from live execution while producing evidence that can flow into research and paper validation.

## Key folders and files

- `Meridian.Backtesting.csproj` - backtesting runtime project boundary.
- `BacktestStudioContracts.cs` - Backtest Studio request, handle, status, and engine contracts.
- `BacktestStudioRunOrchestrator.cs` - records accepted, terminal, cancelled, and failed Studio
  runs through strategy-run lineage.
- `MeridianNativeBacktestStudioEngine.cs` - native Meridian engine adapter that returns canonical
  SDK backtest results.
- `BacktestPreflightService.cs` - backtest-scoped preflight checks for date range, replay-data
  coverage, execution-model compatibility, and optional Security Master validation.
- Runtime and replay implementation files for historical simulation.

## Important workflows

Principal-paydown position adjustments consume Instruments `FactorPaydownProjectionService`, reduce
total basis by the projected monetary principal, and then recompute per-unit basis. The historical
adjuster therefore uses the same held-face/factor economics as the governed production proof while
remaining a rebuildable simulation rather than an accounting write path.

Use this module for strategy backtests, simulation runtime behavior, and backtesting evidence.
Backtest Studio engines must return canonical SDK `BacktestResult` instances so native Meridian and
external engine runs can flow through the same strategy-run repository, comparison, diff, and
portfolio drill-in surfaces.
Backtest Studio requests may carry operator-facing acceptance criteria plus retained evidence,
accounting-record, approval, paper-validation, and governed-report references. The orchestrator
persists those links onto the shared strategy-run entry at start and preserves them when the run
completes, keeping W6 limited to evidence linkage rather than broad Studio UX expansion.

## Execution realism and result trustworthiness

- `BacktestRequest.FillTiming` defaults to `NextBar`: an order can only fill against events
  strictly later than the event that generated it, eliminating same-bar look-ahead. Under
  next-bar timing a `Day` order expires at the end of the first day on which its symbol traded
  after submission (its intended session), so daily-bar strategies still fill. `SameBar` restores
  the legacy behaviour and is flagged in the result's bias disclosure.
- `BacktestRequest.FillConservatism` defaults to `Conservative`: limit orders require the bar to
  trade strictly through the limit (gaps fill at the open; a bare touch does not fill), and stop
  fills anchor to the worse of the stop and the open so they can never beat the stop price.
  `Optimistic` restores legacy touch/midpoint behaviour and is flagged in the bias disclosure.
- `BacktestRequest.DelistingPolicy` defaults to `LiquidateAtLastPrice`: positions in symbols whose
  data goes silent for more than `DelistingGraceDays` are force-liquidated at the last observed
  price adjusted by `DelistingHaircutPercent`, instead of being marked at a stale price forever.
  Every forced liquidation is recorded on the result.
- Every `BacktestResult` carries a `BiasDisclosureReport` (fill timing, limit/stop realism,
  universe provenance/survivorship, corporate-action handling, Security Master gaps, delisting
  liquidations, in-sample caveat). UI surfaces render it as a bias-disclosure panel next to the
  numbers; keep it populated when adding new engine paths.
- `WalkForward.WalkForwardService` wraps `BatchBacktestService` in a walk-forward / out-of-sample
  harness: per rolling (or anchored) training window it sweeps the parameter grid, selects the best
  set by a configurable objective, evaluates it once on the adjacent unseen test window, and stitches
  the test windows into aggregate OOS metrics with train-vs-test degradation reporting.

## API / contract notes

- `BacktestRequest.OrderBookQueueAheadFraction` feeds `OrderBookFillModel` when order-book
  execution is selected. The model infers a bounded queue-ahead quantity from each visible L2
  level, reducing fillable depth without retaining per-order book state across large replay windows.
- `BacktestRequest.MaxParticipationRate` caps per-bar participation for bar-midpoint and
  market-impact fills. A value of `0` preserves the historical full-fill behavior; positive values
  leave oversized market-impact orders working across bars when partial fills are allowed.
- `MeridianNativeBacktestStudioEngine` stamps native engine output through
  `CanonicalBacktestResultNormalizer.FromNative`, matching the metadata contract used by
  QuantConnect Lean imports.
- `BacktestStudioRunRequest`, `BacktestStudioRunHandle`, `BacktestStudioRunStatus`, and
  `IBacktestStudioEngine` live in this module so native and external Studio engines share the
  Backtesting-owned orchestration contract instead of depending on the application layer.
- `BacktestStudioRunRequest` carries the W6 evidence-loop metadata consumed by
  `BacktestStudioRunOrchestrator`: operator acceptance criteria, retained evidence references,
  accounting-record references, approval references, paper-validation references, and governed
  report references.
- `BacktestPreflightService` consumes the shared `ISecurityValidationGateService` contract from
  `Meridian.Contracts.Services`, keeping Security Master trust-gate validation optional for hosts
  while preserving fail-closed preflight behavior when the gate reports blocking issues.

## Benchmarks and performance

- `MultiSymbolMergeEnumerator` uses a heap for multi-symbol replay and a single-stream fast path
  for one-symbol runs, avoiding per-event heap churn on large historical windows while preserving
  cancellation and enumerator disposal.
- Corporate-action adjustment in `BacktestEngine` adjusts historical bars one event at a time after
  cached Security Master action lookup, so mixed bar/trade/depth streams do not buffer replay
  windows before yielding downstream events.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-BACKTESTING -->
| Roadmap item | Title |
| --- | --- |
| `W3-CONT-001` | Research to paper continuity |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-BACKTESTING -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj --logger "console;verbosity=normal"
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj --filter "FullyQualifiedName~BacktestPreflightServiceTests|FullyQualifiedName~MeridianNativeBacktestStudioEngineTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~BacktestStudioRunOrchestratorTests" --logger "console;verbosity=normal"
```

## Change rules

Keep backtesting deterministic and separate from live broker actions.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- `archive/docs/plans/waves-2-4-operator-readiness-addendum.md`
