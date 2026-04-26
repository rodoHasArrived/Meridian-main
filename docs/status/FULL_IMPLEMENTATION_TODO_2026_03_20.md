# Full Implementation Backlog (Non-Assembly Scope)

**Last Updated:** 2026-04-26
**Status:** Active normalized backlog
**Purpose:** Single current backlog for finishing the remaining planned non-assembly work

This document is the normalized execution backlog for the repository's remaining product and structural work outside Phase 16 assembly/SIMD optimization.
It is subordinate to [`ROADMAP.md`](ROADMAP.md): the tracks below are execution buckets that map to the canonical Wave 1-6 order and optional tracks, not an independent strategy. Status labels and target dates are canonical in [`PROGRAM_STATE.md`](PROGRAM_STATE.md).

Use it with:

- `ROADMAP.md` for wave and phase sequencing
- `FEATURE_INVENTORY.md` for current-vs-target capability status
- `IMPROVEMENTS.md` for completed improvement history
- `../plans/governance-fund-ops-blueprint.md`
- `../plans/quant-script-environment-blueprint.md`
- `../plans/l3-inference-implementation-plan.md`

---

## Canonical Program State

Program wave status is canonical in [`PROGRAM_STATE.md`](PROGRAM_STATE.md). Any wave status wording in this file is explanatory context only.

<!-- program-state:begin -->
| Wave | Owner | Status | Target Date | Evidence Link |
| --- | --- | --- | --- | --- |
| W1 | Data Operations + Provider Reliability | Done | 2026-04-17 | [`production-status.md#provider-evidence-summary`](production-status.md#provider-evidence-summary) |
| W2 | Trading Workstation | In Progress | 2026-05-29 | [`ROADMAP.md#wave-2-web-paper-trading-cockpit-completion`](ROADMAP.md#wave-2-web-paper-trading-cockpit-completion) |
| W3 | Shared Platform Interop | In Progress | 2026-06-26 | [`ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity`](ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity) |
| W4 | Governance + Fund Ops | In Progress | 2026-07-24 | [`ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline`](ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline) |
| W5 | Research Platform | Planned | 2026-08-21 | [`ROADMAP.md#wave-5-backtest-studio-unification`](ROADMAP.md#wave-5-backtest-studio-unification) |
| W6 | Execution + Brokerage Integrations | Planned | 2026-09-18 | [`ROADMAP.md#wave-6-live-integration-readiness`](ROADMAP.md#wave-6-live-integration-readiness) |
<!-- program-state:end -->

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
- Trading dashboard promotion gate now supports both **approval** and **rejection** decisions with explicit operator rationale fields wired to the promotion API
- Wave 2 trading-readiness contract wired through `/api/workstation/trading/readiness` and `TradingOperatorReadinessDto`, covering session, replay, control, promotion, DK1 trust-gate packet/sign-off posture, brokerage-sync, work-item, and warning posture
- Promotion approvals now use the canonical `PromotionApprovalChecklist`; `Backtest -> Paper` approvals require DK1 trust-packet, run-lineage, portfolio/ledger-continuity, and risk-control review, while `Paper -> Live` additionally requires live-override review
- Strategy lifecycle control endpoints: `/api/strategies/status`, `/api/strategies/{id}/status`, `/api/strategies/{id}/pause`, `/api/strategies/{id}/stop`
- `PaperSessionPersistenceService`, `IPortfolioState`, `IOrderGateway`, `IOrderManager`, `StrategyLifecycleManager` fully wired in DI
- Brokerage gateway framework complete: `IBrokerageGateway`, `BaseBrokerageGateway`, `BrokerageGatewayAdapter`, plus Alpaca/IB/StockSharp adapter implementations
- WPF shell modernization: native Fluent theme (`ThemeMode="System"`, PR #524), SVG icon set replacing emoji glyphs (PR #512), LiveCharts2 candlestick charting on Charting page (PR #522)
- Zero-API-key startup: Synthetic provider default when no credentials are present (PR #513)
- Route/health endpoint reliability: duplicate DFA route definitions and duplicate health endpoint registrations resolved (PRs #521, #519)
- Workflow guide and live screenshots: `docs/WORKFLOW_GUIDE.md` with UI screenshots (PR #511); CI screenshot-refresh workflow (PR #515)

Implemented foundations now available to build on:

- workspace categories aligned around `Research`, `Trading`, `Data Operations`, and `Governance`
- current working-tree WPF shell consolidation now includes metadata-driven shell navigation, workspace shell pages, deep-page hosting, context strips, shell/navigation smoke coverage, and focused tests for Batch Backtest, Position Blotter, Notification Center, Welcome, workspace queue tone styles, and the workspace shell context strip; it should support Track B, Track C, and Track F workflows rather than become a separate roadmap lane
- delivered Security Master platform seam with shared coverage/provenance flowing across workstation and governance surfaces
- DK1 pilot parity now has an emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` contract plus generated parity-packet artifacts; the latest packet is `ready-for-operator-review`, and operator sign-off must stay synchronized across the validation script, provider matrix, runbook, and readiness dashboard
- coordination services and lease/ownership primitives for future multi-instance work
- paper trading gateway and brokerage adapter layer with REST surface fully wired
- promotion workflow service and endpoint layer providing the `Backtest → Paper → Live` execution path

The remaining backlog is therefore about turning those foundations and delivered seams into a complete operator-facing product.

---

## Backlog Tracks

### Track A / Wave 1: Closed trust-gate maintenance

Goal: preserve the closed Wave 1 trust gate without widening scope, and keep the currently shipped platform easier to trust and easier to operate.

Open work:

- keep the active provider set fixed at Alpaca, Robinhood, and Yahoo across roadmap, status, matrix, and script surfaces
- keep Robinhood runtime-bounded evidence explicit and current under `artifacts/provider-validation/robinhood/`
- rerun `run-wave1-provider-validation.ps1` when provider, checkpoint, or Parquet proof surfaces change
- keep deferred providers labeled consistently outside the active Wave 1 gate
- continue adjacent backtesting-engine and strategy-run persistence coverage expansion without reopening Wave 1 scope

Exit signal:

The active gate for Alpaca, Robinhood, and Yahoo stays reproducible through `run-wave1-provider-validation.ps1`; checkpoint and Parquet rows stay closed in repo tests; and deferred providers remain clearly outside the active Wave 1 claim.

### Track B / Wave 2: Paper-trading cockpit hardening

Goal: harden the existing execution primitives, brokerage adapters, and wired REST/dashboard flows into a dependable operator cockpit in the web workstation.

Open work:

- tighten the existing live positions, open orders, fills, P&L, and risk panels in the React dashboard wired to `/api/execution/*`
- expose promotion evaluation result, required approval-checklist state, session state, replay state, DK1 trust-gate packet/sign-off posture, brokerage-sync posture, operator work items, and execution-control state from the shared trading-readiness contract with clearer acceptance criteria in the dashboard
- verify paper-trading session persistence and replay from persisted order history under realistic operator scenarios
- extend broker validation beyond the checked-in Alpaca execution path to additional live adapters (IB, StockSharp)

Primary anchors:

- `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs`
- `src/Meridian.Execution/`
- `src/Meridian.Ui/dashboard/`

Exit signal:

A strategy researched in backtest can be promoted to paper trading through one connected workflow in the web workstation, with live positions and fills visible.

### Track C / Wave 3: Shared run / portfolio / ledger continuity

Goal: strengthen the shared run, portfolio, ledger, cash-flow, and reconciliation model so strategy workflows feel durable and continuous across workspaces.

Open work:

- extend run history beyond backtest-first into paper and live-adjacent results
- deepen portfolio drill-ins: attribution, drawdown breakdown, cash-flow, and trade-level analysis
- build portfolio comparison across multiple strategy runs
- surface ledger and reconciliation continuity in the web dashboard
- strengthen strategy lifecycle test coverage

Primary anchors:

- `src/Meridian.Strategies/Services/`
- `src/Meridian.Ledger/`
- `src/Meridian.Contracts/Workstation/`

Exit signal:

Research, trading, and governance rely on one consistent shared run, portfolio, ledger, cash-flow, and reconciliation model across backtest, paper, and live-adjacent runs.

### Track D / Wave 5: Backtest Studio unification

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

### Track E / Wave 6: Live integration readiness

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

### Track F / Wave 4: Governance and fund-operations productization

Goal: productize governance and fund-operations workflows on top of the delivered Security Master seam and direct-lending foundations.

Open work:

- extend the delivered Security Master seam into broader account/entity and strategy-structure workflows
- deepen portfolio and ledger surfaces into first-class governance tooling
- add multi-ledger, trial-balance, and cash-flow views
- implement reconciliation workflows and governed reporting/report-pack generation

Primary anchors:

- `../plans/governance-fund-ops-blueprint.md`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Contracts/SecurityMaster/`
- `src/Meridian.Storage/SecurityMaster/`

### Track G: QuantScript _(implemented)_

Goal: ship the QuantScript capability as a real project, not only a blueprint.

**Status: Closed.** The QuantScript project has been fully implemented:

- `src/Meridian.QuantScript/` — Roslyn scripting API, `PriceSeries`/`ReturnSeries` domain types, `StatisticsEngine`, `BacktestProxy`, `QuantDataContext`, `PlotQueue`
- `src/Meridian.Wpf/Views/QuantScriptPage.xaml` + `QuantScriptViewModel` — AvalonEdit editor, three-column layout, Console/Charts/Metrics/Trades/Diagnostics result tabs, ScottPlot charting
- `tests/Meridian.QuantScript.Tests/` — compiler, runner, statistics engine, plot queue, portfolio builder tests
- `scripts/example-sharpe.csx` — sample script

Remaining optional work: deeper workflow integration and expanded sample script library.

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

### Track K: Phase 1.5 — Preferred & Convertible Equity domain extension

Goal: extend the F# Security Master domain model to support preferred and convertible equity classifications as a foundation for Phase 1.5 UFL Equity V2.

Open work:

- add `EquityClassification` discriminated union to `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- add `PreferredTerms` record: `DividendRate`, `DividendType` (Fixed/Floating/Cumulative), `RedemptionPrice`, `RedemptionDate`, `CallableDate`, `ParticipationTerms`, `LiquidationPreference`
- add `ConvertibleTerms` record: underlying, conversion ratio, conversion price, and date windows
- add `LiquidationPreference` union: `Pari`, `Senior of decimal`, `Subordinated`
- update `EquityTerms` to include optional `Classification: EquityClassification option` field
- add unit tests validating term constraints (backward-compatible with existing common equity flows)
- update `docs/ai/claude/CLAUDE.domain-naming.md` with naming conventions (`PrefShrDef`, `ConvPrefDef`, `DivTr`, `RedTr`, `CallTr`, `ConvTr`)

Primary anchors:

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.FSharp/Domain/SecurityClassification.fs` (partial foundation already present: `PreferredEquity`, `PreferredShare`)
- `issues/phase_1_5_1_add_equityclassification_discriminator_and_preferredterms_domain_model.md`
- `PROJECTS/Phase_1.5_Preferred_and_Convertible_Equity_Support.md`
- `docs/plans/ufl-equity-target-state-v2.md`

Exit signal:

All acceptance criteria in the issue file are checked: discriminated union, term records, unit tests, and naming doc update complete.

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

### Wave 1 _(closed, maintain)_

- Track A: Closed trust-gate maintenance

### Wave 2

- Track B: Paper trading cockpit web UI and promotion workflow

### Wave 3

- Track C: Portfolio and strategy tracking depth

### Wave 4

- Track F: Governance and fund-operations productization on top of Security Master

### Wave 5

- Track D: Backtest Studio unification

### Wave 6

- Track E: Live integration readiness

### Optional Wave

- Track G: QuantScript _(implemented — deeper workflow integration and sample scripts remain)_
- Track H: L3 inference/simulation foundation
- Track I: Multi-instance coordination
- Track J: Remaining structural/documentation closure
- Track K: Phase 1.5 preferred/convertible equity domain extension _(F# domain layer — all acceptance criteria open)_

---

## Practical Definition of Done

The repository can reasonably claim core operator-readiness when all of the following are true:

1. **Wave 1 gates:** the active gate for Alpaca, Robinhood, and Yahoo is documented in executable suites or committed runtime artifacts, and checkpoint plus Parquet proof remains closed in repo tests.
2. **Wave 2 gates:** the web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest → Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen readiness claims, but they are not prerequisites for the core operator-ready baseline above.

Until then, Meridian is best described as feature-rich and structurally strong, but still in active productization rather than fully complete.
