---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-BACKTESTING
path: src/Meridian.Backtesting
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-10-21
---

# src/Meridian.Backtesting

## Purpose

Multi-symbol replay compares full UTC ticks and preserves requested-symbol order for equal instants,
including mixed flat and per-symbol storage layouts. Stream initialization and cancellation attempt
disposal of every opened enumerator. When initialization and disposal both fail, the aggregate
preserves the initialization error alongside every cleanup error.

Backtesting contains runtime support for historical strategy simulation and replay-oriented research workflows.

## Layer responsibility

This layer should keep simulation behavior isolated from live execution while producing evidence that can flow into research and paper validation.

Security Master dependencies come from `Meridian.Contracts.SecurityMaster`, with no Application
project reference. The engine accepts separate identity and trading-parameter query services;
`CorporateActionAdjustmentService` requires only the resolver and corporate-action query interface
and implements the canonical live-position adjuster. Existing aggregate query callers retain the
identity fallback, while hosts can compose the narrow services independently.
The original aggregate-query constructor remains available with its original CLR signature and
optional arguments for existing compiled callers.

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

The native engine resolves lot denomination from each available Security Master definition
before replay. Fund definitions retain `Units` and equities retain `Shares`, both with multiplier
1. Definitions requiring face, notional, commitment, currency, or contract pricing are refused
until a compatible pricing model exists; replay cannot silently apply equity calculations to
them. Legacy runs without a resolved security definition explicitly retain the symbol-only
equity assumption. Lot snapshots require explicit units and positive multipliers.

Long sales and short covers use Ledger's `LotBasisRelief` allocator over the shared decimal
consumption walk. Composite lots supply their exact retained component basis; full closure never
reconstructs that basis from a rounded entry quote. FIFO/LIFO/HIFO ordering and the simulation's
SpecificId-to-FIFO fallback are preserved. This calculation convergence does not manufacture
canonical SecurityIds or acquisition FX, authorize short posting, or replace retained Ledger lots.

Principal-paydown position adjustments consume Instruments `FactorPaydownProjectionService`, reduce
total basis by the projected monetary principal, and then recompute per-unit basis. The historical
adjuster uses scalar compatibility economics: it does not retain the prior lot factor or original
face authority required for canonical pro-rata basis relief and premium/discount realization. This
path is a rebuildable simulation, not governed lot-basis parity or an accounting write path.

The live-position adjuster's additive retained-basis overload accepts the paper lot book's exact
total and returns `AdjustedTotalCostBasis`. Splits retain that total; sequential principal paydowns
subtract projected principal before a display unit quote is derived. The old overload and custom
implementers remain compatible, with the absent exact-total field omitted from JSON. Existing
compatibility merger economics are unchanged; this API is not a governed corporate-action journal.

Corporate-action-adjusted engine runs capture the exact symbol/date-filtered JSONL event window
once per shared storage root, prepare one immutable adjustment plan per symbol from its complete bar
history and one Security Master query snapshot, and execute from that captured window. Every symbol
sharing a source is captured before any of those plans is prepared. `BacktestRequest.To` is the economic cutoff for
effective actions; it is not a transaction-time Security Master revision boundary. Only confirmed
(or legacy confirmed-by-default), non-cancelled actions effective by that cutoff are eligible.

Portfolio asset events fan out to every brokerage account holding the source symbol. Whole-unit
transformations apportion successor shares through exact FIFO entitlements. Fractional entitlements
carry their own basis across lot boundaries, and a combined whole share receives a deterministic
composite lot/fill identity plus immutable component basis provenance. Chained actions scale the
original components instead of collapsing them into the intermediate synthetic lot. Successor notes
retain operator text and replace generated composite summaries with only the current component
lineage; a single-component successor no longer claims the prior composite's contributors. Cash-in-lieu
disposals relieve the remaining fractional securities basis or short payable and recognize the
resulting account-scoped gain or loss in both the ledger and asset-event cash-flow evidence, including
when no successor position remains. Metrics consume the final account snapshots' booked closures
and retained cash-in-lieu disposal evidence. Attribution uses exact relieved basis and account
positions, preserving successor economics and offsetting account positions. Realized symbol
attribution also includes booked per-share corporate-action consideration, income, and fees under
the source symbol; cash-in-lieu principal is excluded while its retained disposal gain is included.
Completed outcomes are
counted per account, symbol, and closing order; partial fills and lot slices of one closing order
remain one outcome, breakeven outcomes count, and each fractional disposal adds one outcome.
Win rate and profit factor deduct the acquisition and closing fees allocated to those disposals.
Acquisition fees follow component entitlements through transformations and stay separate from
securities basis. Older snapshots without retained closures cannot certify trade outcomes; metrics
do not reconstruct missing accounting evidence from fills. External summary imports retain their
own declared coverage instead of invoking this native calculation. A
source-to-destination collision with opposite position directions fails before mutation because it
requires explicit close/cover economics. Short lots preserve explicit direction in position,
account, and aggregate snapshots. Legacy adjustment services that cannot enforce the engine's
effective-through boundary fail closed until they implement prepared plans.

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

## API / contract notes

- Drawdown currency and percentage maxima are tracked independently, including the decline from
  initial capital to the first snapshot. Recovery days and Calmar follow the worst percentage
  episode, matching live-run metrics and the promotion threshold contract.
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

## Unitized lots

`SimulatedPortfolio` keeps open and closed lot quantities as `decimal` and carries each lot's unit
kind and multiplier, so fractional crypto units, contracts and face value share one lot shape with
execution and the ledger (`IUnitizedLot`).

## Benchmarks and performance

- `BacktestEngine` opens one replay per distinct resolved storage root. Symbols using the flat or
  provider/date fallback share one external sort, which includes only their selected events rather
  than copying the full data root for each symbol. Per-symbol directories remain scoped sources.
- `MultiSymbolMergeEnumerator` uses a heap across replay sources and a single-stream fast path
  when all symbols share one source, avoiding per-event heap churn on large historical windows
  while preserving cancellation, full timestamp precision, symbol ties, and enumerator disposal.
- Corporate-action adjustment in `BacktestEngine` uses a two-pass path: it persists the exact
  filtered mixed-event stream to a temporary JSONL snapshot while retaining its historical bars for
  plan preparation, then streams execution from that snapshot. This prevents preparation/execution
  drift without retaining non-bar market events in memory. Unix snapshot files are created with
  owner-only read/write permissions and are removed on completion, failure, or cancellation.

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
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj --filter "FullyQualifiedName~CorporateActionAdjustmentServiceTests" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~SecurityMasterContractOwnershipTests" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj --logger "console;verbosity=normal"
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj --filter "FullyQualifiedName~BacktestPreflightServiceTests|FullyQualifiedName~MeridianNativeBacktestStudioEngineTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~BacktestStudioRunOrchestratorTests" --logger "console;verbosity=normal"
```

## Change rules

Keep backtesting deterministic and separate from live broker actions.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- [`archive/docs/plans/waves-2-4-operator-readiness-addendum.md`](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/waves-2-4-operator-readiness-addendum.md)
