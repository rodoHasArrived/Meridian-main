---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-BACKTESTING
path: src/Meridian.Backtesting
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-11-12
---

# src/Meridian.Backtesting

Simulated security buys, disposals, short sales/covers, return of capital and fractional
corporate-action settlements retain producer-supplied investing cash-flow attribution when
their journals are first posted. Reported cash flows therefore use the actual cash movement;
carrying-basis relief and realized gain/loss do not become separate operating cash flows.

With short selling disabled, `SimulatedPortfolio` rejects any sell that would leave the position net short, including a sell larger than the current long (long 100, sell 150); it previously rejected only sells from a flat or short position.

An annualised return too large for `decimal` (a 25% one-day gain compounds past it) is reported capped at 1e12 instead of aborting the metrics build. Walk-forward stitched out-of-sample total and annualised returns use the same cap.

`MarketImpactFillModel` fills an all-or-none order completely or not at all; a limit order whose later slices cross the limit no longer returns a partial fill when partial fills are disallowed.

XIRR is computed from investor-boundary flows only (initial capital out, terminal equity in); internal trade, commission and dividend flows are already inside equity. A loss of 100% or more reports an annualised return of -100% instead of aborting the metrics build.

`MarketImpactFillModel` leaves an order working on a zero-volume bar (no traded liquidity) and caps
square-root impact at `MaxImpactFraction` (50%), so a thin bar can no longer drive a sell fill price
to zero or below.

Reducing an OCO sibling after the other leg fills keeps the sibling's own filled shares in its
total `Quantity`, so a partly filled bracket exit keeps working the full remaining position instead
of losing the already-filled amount a second time.

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

`AddMeridianBacktesting` composes `CorporateActionAdjustmentService` from the corporate-action
query and resolver when no adjustment service is registered, retaining adjusted-price support
across browser, CLI and WPF hosts. Identity and corporate-action queries also enable the broader
automatic portfolio-event source. The factory retains `SecurityMasterAssetEventSource` as the
narrow explicit opt-in compatibility source; request policy selects one population path before
replay. An unavailable Security Master remains visible in the run's bias disclosure.

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

`SecurityTradePostingProjector` supplies the shared long/short trade and commission line
projection from the simulator's exact lot facts. The simulator attaches investing attribution
before posting each projected trade; commissions retain their separate operating cash flow.
Lot selection, corporate-action basis relief and day-end integrity comparisons remain simulation
responsibilities, without reconstructing carrying value from rounded quotes or realized P&L.

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

`SimulatedPortfolio` resolves posting accounts through the shared `IChartOfAccounts` and
`StandardChartOfAccounts` role registry, retaining existing account identities and brokerage/symbol
scopes for capital, fills, financing, income, and corporate actions. Fill and commission journal
lines consume the shared `SecurityTradePostingProjector` with each leg's exact relieved basis or
short proceeds; simulation keeps its descriptions, activity types, and fill/order references.
The simulator retains short-payable accounting; it does not migrate historical execution journals
that record short positions as credits to Securities.

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
currently captures none, so it fails closed rather than serving as W6 closure evidence. Expanded
strategy formulation and validation are planned under `W13-STRAT-001`, with shared orchestration
tracked by `PRD-106`; the scope decision does not close these implementation or acceptance gaps.
See the [expanded objectives](../../docs/product/implementation-todo-list.md#expanded-strategy-and-order-management-objectives).

## Execution realism and result trustworthiness

- `SecurityMasterBacktestAssetEventSource` connects the existing provider ingest/proposal pipeline
  to native portfolio replay. `ApplyStoredCorporateActions` defaults to true when the host has
  Security Master query services, unless explicit compatibility mode is selected below. Only
  confirmed, effective, non-cancelled revisions become automatic events. Identical economics are
  deduplicated and conflicting instructions fail before
  replay. Stored splits, USD dividends, return of capital, mergers and ticker changes are supported.
  Successor securities are followed after mergers, and later actions use the successor ticker.
  Unsupported action types, missing successor terms, non-USD cash and ambiguous same-day successor
  ordering require reviewed manual events with automatic loading disabled.
- `PopulateAssetEventsFromSecurityMaster = true` selects the explicit compatibility path instead
  of broad automatic loading. A supplied `AssetEvents` list, including an empty list, takes
  precedence. Without that list, the path requires `AdjustForCorporateActions = false` and a
  configured source. Its `SecurityMasterAssetEventSource` maps confirmed cash distributions and
  share splits; it does not map mergers, spin-offs or other position transformations. Selecting
  this path never also calls the broad source.
- Portfolio action accounting uses unadjusted prices, including when explicit `AssetEvents` are
  supplied. Already-adjusted bars are refused for an event-bearing replay so splits and cash cannot
  be counted twice. Stored dividends credit ex-date holders on the ex-date; payable-date settlement
  is not modeled. This economic replay uses currently retained confirmed Security Master revisions
  and makes no historical transaction-visibility claim. Provider proposals still require the
  existing ingest acceptance workflow; backtests never approve provider observations.

Every native day-end snapshot reconciles as-of trial balance, per-account cash, long lot basis,
short proceeds, and transfer clearing balances. Differences remain on
`BacktestResult.LedgerIntegrityBreaks`; `BacktestRequest.FailOnLedgerIntegrityBreak` aborts with
inspectable `LedgerIntegrityException.Breaks`. Comparisons use the shared ledger balance tolerance
and include residual accounts for closed positions. Return-of-capital distributions reduce each
long lot/component's exact basis to zero before recognizing excess gain. `BasisReturned` stays
separate from income in cash-flow evidence and symbol attribution; short payments remain expenses.

Financing accrues on every calendar date using ACT/365 fixed. Sharpe, Sortino, rolling replay
metrics, and stitched walk-forward metrics use the same 365-observation annualization because
the native snapshot series includes weekends. Trade cash flows contain only consideration;
commission cash flows carry fees separately, and their sum reconciles to the cash movement.
Fill models apply fixed, minimum, and maximum commissions cumulatively per order across all
accepted partial fills rather than charging each execution as a new order.

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

### Retained sweep comparison identity

Studio sweep records retain each run's parameter values while a versioned experiment fingerprint
excludes point values and includes invariant window, universe, native request and cost configuration.
The native request uses source-generated JSON with the existing `sweep-v2` serializer contract;
property names, enum representation, decimal scale and dictionary order retain their hash semantics.
Read models additionally separate strategy, engine, dataset, feed and mode. Legacy fingerprints remain
exploratory and are not heuristically merged. Objective rankings honor the named supported metric;
unknown objectives do not substitute final equity. Returned parameter coordinates support the browser
sensitivity grid, with explicit history-limit and absent expected-grid warnings. This is retained-run
analysis, not a new supported host sweep-launch workflow or promotion authorization.

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
- The bar-based fill models honour time-in-force the same way the order-book model does. An
  `ImmediateOrCancel` or `FillOrKill` order is resolved on the first bar it is executable: IOC keeps
  what filled and cancels the remainder, FOK fills completely or is cancelled with no fill, and
  neither stays working on later bars. An order with `AllowPartialFills = false` never ends
  partially filled, including when market-impact slices are skipped for crossing the limit price.
  The replay engine acts on that result: an order the fill model cancels, and an immediate order
  whose generated fills the account rejected, is removed from the pending set and cannot fill on a
  later event. This applies to every fill model, including the order-book model.
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
