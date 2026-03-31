# Full Implementation Backlog (Non-Assembly Scope)

**Last Updated:** 2026-03-31
**Status:** Active normalized backlog
**Purpose:** Single current backlog for finishing the remaining planned non-assembly work

This document is the normalized execution backlog for the repository's remaining product and structural work outside Phase 16 assembly/SIMD optimization.

Use it with:

- `ROADMAP.md` for wave and phase sequencing
- `FEATURE_INVENTORY.md` for current-vs-target capability status
- `IMPROVEMENTS.md` for completed improvement history
- `../plans/governance-fund-ops-blueprint.md`
- `../plans/quant-script-environment-blueprint.md`
- `../plans/l3-inference-implementation-plan.md`

---

## Current Baseline

The repo is no longer blocked by the earlier partial items that used to dominate the backlog.

Closed platform work:

- C3 provider lifecycle consolidation for the active platform baseline
- G2 end-to-end trace propagation for the current ingestion/storage path
- I3 checked-in config schema generation and validation
- J8 canonicalization drift reporting and fixture-maintenance workflow
- Paper-trading cockpit REST endpoints wired: `/api/execution/account`, `/api/execution/positions`, `/api/execution/portfolio`, `/api/execution/orders`, `/api/execution/health`, `/api/execution/capabilities`
- Paper-trading session management endpoints: `/api/execution/sessions` (create, list, detail, close)
- `Backtest → Paper → Live` promotion workflow endpoints: `/api/promotion/evaluate/{runId}`, `/api/promotion/approve`, `/api/promotion/reject`, `/api/promotion/history`
- Strategy lifecycle control endpoints: `/api/strategies/status`, `/api/strategies/{id}/status`, `/api/strategies/{id}/pause`, `/api/strategies/{id}/stop`
- `PaperSessionPersistenceService`, `IPortfolioState`, `IOrderGateway`, `IOrderManager`, `StrategyLifecycleManager` fully wired in DI
- Brokerage gateway framework complete: `IBrokerageGateway`, `BaseBrokerageGateway`, `BrokerageGatewayAdapter`, plus Alpaca/IB/StockSharp adapter implementations

Implemented foundations now available to build on:

- workspace categories aligned around `Research`, `Trading`, `Data Operations`, and `Governance`
- Security Master application/storage/domain foundation
- coordination services and lease/ownership primitives for future multi-instance work
- paper trading gateway and brokerage adapter layer with REST surface fully wired
- promotion workflow service and endpoint layer providing the `Backtest → Paper → Live` execution path

The remaining backlog is therefore about turning those foundations into a complete operator-facing product.

---

## Backlog Tracks

### Track A: Provider confidence and current-functionality hardening

Goal: make the currently shipped platform easier to trust and easier to operate.

Open work:

- expand Polygon replay coverage across more feeds and edge cases
- strengthen NYSE shared-lifecycle regression coverage
- keep IB runtime/bootstrap guidance aligned with the official vendor surface
- keep StockSharp connector/runtime guidance aligned with validated adapters
- expand under-tested provider coverage for TwelveData, Nasdaq Data Link, Alpha Vantage, Finnhub, Stooq, and OpenFIGI
- continue backtesting-engine and strategy-run persistence coverage expansion
- validate backfill checkpoint reliability across providers and longer date ranges

Exit signal:

Every major provider has documented replay/runtime evidence and passes its validation suite. Backfill checkpoints and gap detection are validated across providers and date ranges.

### Track B: Paper trading cockpit web UI

Goal: make the existing execution primitives, brokerage adapters, and wired REST endpoints visible through a real operator cockpit in the web dashboard.

Open work:

- build live positions, open orders, fills, P&L, and risk state panels in the React dashboard wired to `/api/execution/*`
- expose promotion evaluation result and approval controls in the dashboard
- add paper-trading session persistence and replay from persisted order history
- validate brokerage gateway adapters against real vendor APIs (Alpaca, IB, StockSharp)

Primary anchors:

- `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs`
- `src/Meridian.Execution/`
- `src/Meridian.Ui/dashboard/`

Exit signal:

A strategy researched in backtest can be promoted to paper trading through one connected workflow in the web dashboard, with live positions and fills visible.

### Track C: Portfolio and strategy tracking depth

Goal: strengthen portfolio read models and multi-run comparison so strategy research produces durable, comparable results.

Open work:

- extend run history beyond backtest-first into paper and live-adjacent results
- deepen portfolio drill-ins: attribution, drawdown breakdown, trade-level analysis
- build portfolio comparison across multiple strategy runs
- surface ledger reconciliation in the web dashboard
- strengthen strategy lifecycle test coverage

Primary anchors:

- `src/Meridian.Strategies/Services/`
- `src/Meridian.Ledger/`
- `src/Meridian.Contracts/Workstation/`

Exit signal:

Portfolio and strategy tracking are useful for iterative strategy development across backtest, paper, and live-adjacent runs through one consistent model.

### Track D: Backtest Studio unification

Goal: consolidate the native and QuantConnect Lean backtest experiences into one coherent workflow.

Open work:

- unify native and Lean engine results into a common backtest result model
- add strategy comparison and run-diff tooling
- broaden fill model coverage (partial fills, slippage, market impact)
- improve backtest performance for large historical windows

Primary anchors:

- `src/Meridian.Backtesting/`
- `src/Meridian.Backtesting.Sdk/`
- `tests/Meridian.Backtesting.Tests/`

Exit signal:

Backtesting feels like one product regardless of whether the native engine or Lean is used, with consistent result models.

### Track E: Live integration readiness

Goal: validate the brokerage gateway framework against real vendor surfaces and add the execution audit trail needed for live operations.

Open work:

- validate brokerage gateway adapters against live vendor APIs (Alpaca, IB, StockSharp)
- add execution audit trail sufficient for live operations
- define operator controls (circuit breakers, position limits, manual overrides)
- wire `Paper → Live` promotion gate with human-approval controls

Primary anchors:

- `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs`
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBBrokerageGateway.cs`
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpBrokerageGateway.cs`
- `src/Meridian.Execution/Adapters/BaseBrokerageGateway.cs`

Exit signal:

At least one brokerage adapter is validated against a live vendor surface with audit trail.

### Track F: Governance and fund-operations productization

Goal: productize Security Master and the direct lending foundations into operator-facing governance tooling.

Open work:

- productize Security Master beyond its current foundational services
- add account/entity and strategy-structure workflows
- deepen portfolio and ledger surfaces into first-class governance tooling
- add multi-ledger, trial-balance, and cash-flow views
- implement reconciliation workflows and governed reporting/report-pack generation

Primary anchors:

- `../plans/governance-fund-ops-blueprint.md`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Contracts/SecurityMaster/`
- `src/Meridian.Storage/SecurityMaster/`

### Track G: QuantScript

Goal: ship the QuantScript capability as a real project, not only a blueprint.

Open work:

- add the project and test project
- implement the compiler/runner pipeline
- add execution context, parameter discovery, and plotting/output handling
- add a web dashboard or CLI entry surface
- add samples, tests, and operating docs

Reference:

- `../plans/quant-script-environment-blueprint.md`

### Track H: L3 inference and queue-aware execution simulation

Goal: ship the queue-aware simulation stack as a real capability.

Open work:

- add contracts, config, and deterministic fixtures
- build reconstruction and replay-alignment layers
- implement the inference model and execution simulator
- add CLI/API integration
- add calibration, confidence, and degradation behavior
- add tests and operator docs

Reference:

- `../plans/l3-inference-implementation-plan.md`

### Track I: Multi-instance coordination

Goal: finish the optional scale-out story cleanly instead of leaving it half-implied.

Open work:

- turn the coordination and lease primitives into a supported ownership model
- define duplicate-prevention semantics for subscriptions, scheduled work, and backfill execution
- document a supported multi-node topology and failure/recovery model
- add tests or simulations for lease ownership and failover behavior

Primary anchors:

- `src/Meridian.Application/Coordination/`
- `src/Meridian.Core/Config/CoordinationConfig.cs`
- `tests/Meridian.Tests/Application/Coordination/`

### Track J: Structural closure and documentation convergence

Goal: keep the repo coherent as the remaining product work lands.

Open work:

- continue composition-root and startup readability work
- keep typed service/query seams as the default integration boundary
- keep CI/doc generation and planning/status docs synchronized
- archive newly historical planning material once it truly stops being active guidance

References:

- `../plans/codebase-audit-cleanup-roadmap.md`
- `../plans/readability-refactor-roadmap.md`

---

## Recommended Delivery Order

### Wave 1 *(active)*

- Track A: Provider confidence and hardening

### Wave 2

- Track B: Paper trading cockpit web UI and promotion workflow

### Wave 3

- Track C: Portfolio and strategy tracking depth

### Wave 4

- Track D: Backtest Studio unification

### Wave 5

- Track E: Live integration readiness

### Optional Wave

- Track F: Governance and Security Master productization
- Track G: QuantScript
- Track H: L3 inference/simulation foundation
- Track I: Multi-instance coordination
- Track J: Remaining structural/documentation closure

---

## Practical Definition of Done

The repository can reasonably claim core-platform readiness when all of the following are true:

1. Every major provider has documented replay/runtime validation evidence
2. Paper trading is exposed as a full cockpit in the web dashboard (wired to the existing `/api/execution/*` and `/api/promotion/*` endpoints)
3. The `Backtest → Paper` promotion workflow is explicit and auditable through the dashboard
4. Portfolio and run history cover backtest, paper, and live-adjacent results through one consistent model
5. Backfill checkpoint reliability is validated across providers and date ranges

Until then, Meridian is best described as feature-rich and structurally strong, but still in active productization rather than fully complete.
