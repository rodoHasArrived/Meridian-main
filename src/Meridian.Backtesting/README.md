---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-BACKTESTING
path: src/Meridian.Backtesting
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-08-18
---

# src/Meridian.Backtesting

## Purpose

Backtesting contains runtime support for historical strategy simulation and replay-oriented research workflows.

## Layer responsibility

This layer should keep simulation behavior isolated from live execution while producing evidence that can flow into research and paper validation.

## Key folders and files

- `Meridian.Backtesting.csproj` - backtesting runtime project boundary.
- `BacktestStudioContracts.cs` - Backtest Studio request, handle, status, and engine contracts.
- `BacktestStudioRunOrchestrator.cs` - validates and records accepted, terminal, cancelled, and
  failed Studio runs when invoked directly; it is not registered in the workstation host and is
  not the supported W6 operator path.
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
The supported W6 path is the browser Covered Call form into UI Shared
`CoveredCallBacktestService`, which records a tenant/company-scoped pre-execution entry through the
shared strategy-run repository before queueing the native engine. It requires a stable strategy
identity, at least one operator acceptance requirement, and at least one strict canonical
`evidence://evidence-vault/{vaultId}` reference whose retained manifest resolves inside the
authenticated tenant/company scope. Count, value-length, and aggregate budgets are checked before
Vault I/O. The shared store, replay, run detail, review packet, and Trading readiness path preserve
that scope and evidence lineage.

Acceptance text remains a review requirement until the governed Backtest-to-Paper promotion path
records an approved decision with operator, timestamp, audit reference, all four canonical Paper
checklist ids, keyed evidence that exactly matches the source run, and an exact retained Paper child
run in the same scope. Missing, rejected, foreign, or mismatched authority remains review-required
or rejected; a declaration or eligible metric result alone never becomes checklist completion.

`BacktestStudioRunOrchestrator` applies the same contract when called directly and its isolated
tests remain useful contract proof, but no workstation host composes it. The Strategy Designer
endpoint also requires exactly one captured canonical result, while its production compiler
currently captures none, so it fails closed rather than serving as W6 closure evidence. Broader
Studio UX remains deferred.

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
- The engine keeps one authoritative working-order collection in `BacktestContext`. Submit,
  cancellation, contingent cancellation, trigger, partial-fill, IOC/FOK, OCO, expiry, and rejection
  transitions therefore survive the strategy/engine boundary instead of being split between two
  lists. Only fills accepted by `SimulatedPortfolio` reach strategy callbacks, contingents, metrics,
  or results.
- Commission models quote cumulative order economics before portfolio validation and commit state
  only after an accepted fill. Per-order minimums and maximums therefore apply once across partial
  slices, while rejected fills do not consume commission state and terminal orders release their
  accumulator entries.
- Metrics use resolved account opening cash, external investor opening/terminal flows for XIRR, and
  a consistent 365-day calendar basis for snapshots, financing accruals, rolling metrics, and
  walk-forward aggregation. Internal trade, fee, interest, and corporate-action settlements remain
  ledger/cash-flow evidence but are not treated as investor contributions or withdrawals.

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
- `BacktestStudioRunRequest` carries the declaration contract consumed by
  `BacktestStudioRunOrchestrator`: operator acceptance requirements plus categorized retained
  evidence, accounting-record, approval, paper-validation, and governed-report URI declarations.
  Invalid declarations fail before its engine is invoked or a strategy-run entry is recorded, but
  this contract-only path is not host composition, Vault-authority resolution, or W6 closure proof.
- `BacktestPreflightService` consumes the shared `ISecurityValidationGateService` contract from
  `Meridian.Contracts.Services`, keeping Security Master trust-gate validation optional for hosts
  while preserving fail-closed preflight behavior when the gate reports blocking issues.

## Benchmarks and performance

- `JsonlReplayer` performs a k-way merge across physical JSONL and compressed JSONL files using full
  UTC ticks plus stable file order. It fails closed with file/line evidence for malformed, null, or
  per-file time-regressing records. `MultiSymbolMergeEnumerator` then uses a full-tick heap with a
  single-stream fast path, stable stream ties, cancellation, monotonicity checks, and deterministic
  enumerator disposal.
- Corporate-action adjustment prepares one immutable, content-versioned plan per symbol and run,
  pinned to the request end date and built from the complete bar history. The engine performs a
  bars-only first replay pass, then streams the second pass through `plan.Apply`; the concrete plan
  retains actions and adjustment factors rather than the mixed event window, and its shared cache is
  bounded.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-BACKTESTING -->
| Roadmap item | Title |
| --- | --- |
| `W3-CONT-001` | Research to paper continuity |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `W10-PERF-001` | Portfolio and investor return measurement |
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
