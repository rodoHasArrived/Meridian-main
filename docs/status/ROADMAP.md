# Meridian - Project Roadmap

**Last Updated:** 2026-03-24
**Status:** Refocused on core platform functionality
**Repository Snapshot (2026-03-24):** solution projects: 29 | `src/` projects: 21 | test projects: 5 | workflow files: 37

Meridian is a self-hosted trading platform. The active delivery focus is the four core platform pillars: **data collection**, **backtesting**, **real-time execution**, and **portfolio/strategy tracking**. The web dashboard is the current UI surface. The WPF desktop app code is preserved but not in the active build (see `src/Meridian.Wpf/`).

Use this document with:

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) — current-vs-target capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) — normalized backlog

---

## Focus: The Four Core Pillars

### 1. Data Collection

Real-time streaming and historical backfill are the platform's data foundation. Everything downstream — backtesting, execution, strategy tracking — depends on data quality, reliability, and breadth.

**Current state:**
- 90+ real-time streaming sources via IMarketDataClient (Alpaca, IB, NYSE, Polygon, StockSharp, synthetic)
- 10+ historical backfill providers via IHistoricalDataProvider (Stooq, Alpaca, Tiingo, Yahoo, Polygon, FRED, and others)
- WAL + JSONL/Parquet composite sink, tiered hot/warm/cold storage
- Symbol search across 5 providers
- Data quality monitoring with SLA enforcement and sequence validation

**Remaining work:**
- Harden provider confidence: Polygon replay coverage, IB runtime validation, NYSE shared-lifecycle depth, StockSharp adapter breadth
- Expand backfill fallback chains and checkpoint reliability across edge cases
- Strengthen data quality monitoring SLA enforcement and gap reporting
- Improve provider health observability (metrics, alerts, replay evidence)

---

### 2. Backtesting

Tick-level strategy replay with fill models and portfolio metrics is the primary research workflow.

**Current state:**
- Tick-by-tick replay engine with configurable fill models
- Portfolio metrics: Sharpe ratio, max drawdown, XIRR, and full audit trail
- Native backtest engine + QuantConnect Lean integration
- Strategy SDK (`Meridian.Backtesting.Sdk`) for implementing custom strategies
- Run history, comparison, and export

**Remaining work:**
- Unify native and Lean engine experiences into one Backtest Studio workflow
- Broaden fill model coverage (partial fills, slippage, market impact)
- Deepen strategy comparison and run-diff tooling
- Improve backtest performance for large historical windows
- Expand test coverage for replay correctness and fill model edge cases

---

### 3. Real-Time Execution

Paper trading is the primary execution surface. It validates strategies under realistic conditions before any live integration.

**Current state:**
- Paper trading gateway (`Meridian.Execution`) for zero-risk strategy validation
- Order routing abstraction (`IOrderGateway`) designed for live broker integration
- Pre-trade risk rules via `IRiskRule`
- Position and fill tracking

**Remaining work:**
- Build a full paper-trading cockpit: live positions, open orders, fills, P&L, and controls exposed via the web dashboard
- Complete the `Backtest → Paper` promotion workflow with safety gating and audit trail
- Harden risk rule coverage (position limits, drawdown stops, order rate limits)
- Add paper-trading session persistence and replay support
- Design the `Paper → Live` integration point for future broker connectivity

---

### 4. Portfolio & Strategy Tracking

Multi-run comparison, performance attribution, and strategy lifecycle management close the loop from research through operation.

**Current state:**
- `PortfolioTracker` with multi-run performance metrics
- Strategy lifecycle: registration, activation, pause, stop
- `StrategyRunReadService`, `PortfolioReadService`, `LedgerReadService` for shared read models
- Run history browsable via the web dashboard and REST API
- Ledger infrastructure (`Meridian.Ledger`) with double-entry accounting foundation

**Remaining work:**
- Extend run history beyond backtest-first into paper and live-adjacent results
- Deepen portfolio drill-ins: attribution, drawdown breakdown, trade-level analysis
- Build portfolio comparison across multiple strategy runs
- Surface ledger reconciliation in the web dashboard
- Strengthen strategy lifecycle test coverage

---

## Current State

### Complete

- Core event pipeline (channel-based, backpressure, WAL durability)
- Storage layer (JSONL/Parquet, tiered archival, catalog, export)
- 10+ backfill providers with fallback chain
- 90+ streaming sources with data quality monitoring
- Backtesting engine with tick replay and fill models
- Paper trading gateway with risk rules
- Strategy SDK, lifecycle management, and portfolio tracking
- Ledger infrastructure and double-entry accounting foundation
- Symbol search across 5 providers
- Web dashboard serving all core workflows via REST API
- Provider registration, DI composition, route coverage, and observability baseline
- Deployment assets (Docker, k8s, systemd)

### Partial

- Provider confidence: Polygon replay breadth, IB runtime, NYSE shared-lifecycle, and StockSharp breadth need validation depth
- Backfill checkpoint reliability across longer runs and provider-specific edge cases
- Paper trading cockpit surfaces in the web UI (gateway is implemented; dashboard exposure is incomplete)
- `Backtest → Paper` promotion workflow (read services exist; explicit lifecycle flow is not yet wired)
- Portfolio drill-ins and multi-run comparison depth
- Ledger reconciliation exposed through the web dashboard

### Planned

- Full paper-trading cockpit via the web dashboard
- `Backtest → Paper → Live` promotion workflow with audit trail
- Unified Backtest Studio across native and Lean engines
- Strategy comparison and run-diff tooling
- Live broker integration point (post paper-trading validation)

### Optional / Later

- QuantScript runtime and editor
- L3 inference and queue-aware execution simulation
- Governance: multi-ledger, trial balance, cash-flow, report packs
- Security Master productization as a workstation-visible platform layer
- Multi-instance collector coordination and horizontal scale-out
- Phase 16 assembly-level performance optimizations
- WPF desktop app (code in `src/Meridian.Wpf/` — delayed, see docs/development/wpf-implementation-notes.md)

---

## Delivery Waves

### Wave 1: Provider reliability and data confidence *(active)*

The platform's downstream value depends on trustworthy data. Operator confidence in backtesting and execution results depends on provider correctness.

**Focus:**
- Polygon replay coverage across feeds and edge cases
- IB runtime/bootstrap validation against real vendor surfaces
- NYSE shared-lifecycle coverage
- StockSharp connector examples and validated adapters
- Backfill checkpoint reliability and gap detection

**Exit signal:** Every major provider has documented replay/runtime evidence and passes its validation suite.

---

### Wave 2: Paper trading cockpit and promotion workflow

The paper trading gateway exists. The gap is making it visible and usable through the web dashboard with a clear path from backtest to paper.

**Focus:**
- Web dashboard: live positions, open orders, fills, P&L, risk state panels
- `Backtest → Paper` promotion: explicit lifecycle step, audit trail, safety gate
- Paper session persistence and replay
- Risk rule coverage (position limits, drawdown stops, rate limits)

**Exit signal:** A strategy can be researched in backtest and promoted to paper trading through one connected workflow in the web dashboard.

---

### Wave 3: Portfolio and strategy tracking depth

Strengthen the portfolio read models and multi-run comparison so strategy research produces durable, comparable results.

**Focus:**
- Portfolio drill-ins: attribution, drawdown breakdown, trade-level analysis
- Multi-run strategy comparison
- Run history covering paper and live-adjacent results, not only backtest
- Ledger reconciliation in the web dashboard

**Exit signal:** Portfolio and strategy tracking are useful for iterative strategy development, not just single-run review.

---

### Wave 4: Backtest Studio unification

Consolidate the native and Lean backtest experiences into one coherent workflow.

**Focus:**
- Unified Backtest Studio spanning both engines
- Strategy comparison and run-diff tooling
- Broader fill model coverage
- Backtest performance improvements for large historical windows

**Exit signal:** Backtesting feels like one product whether using the native engine or Lean, with consistent result models.

---

### Wave 5: Live integration readiness

Define and implement the `Paper → Live` integration point so broker connectivity can be added without restructuring the execution model.

**Focus:**
- Finalize `IOrderGateway` contracts for live broker adapters
- Add execution audit trail sufficient for live operations
- Define operator controls (circuit breakers, position limits, manual overrides)

**Exit signal:** The execution architecture is ready for a live broker adapter with minimal structural change.

---

### Optional Wave: Advanced research and scale

Depth multipliers that require a stable platform foundation to deliver value.

**Focus:**
- QuantScript runtime and editor
- L3 inference and queue-aware simulation
- Multi-instance collector scale-out
- Phase 16 assembly-level performance work

**Exit signal:** These tracks deepen the platform; they are not prerequisites for core operator value.

---

## Risks

- **Provider trust is a gating dependency.** Meridian should not overclaim operator readiness where replay/runtime evidence is still thin.
- **Paper trading cockpit must be wired before Paper → Live work starts.** The gateway without a cockpit is incomplete operator tooling.
- **Backfill reliability directly affects backtest quality.** Data gaps silently corrupt results — checkpoint and gap-detection hardening is not optional.
- **Test coverage must grow with the platform.** Strategy correctness, fill model edge cases, and provider adapters need explicit regression coverage.

---

## Non-Assembly Release Gates

Meridian can claim core-platform readiness when:

1. Every major provider has documented replay/runtime validation evidence
2. Paper trading is exposed as a full cockpit in the web dashboard
3. The `Backtest → Paper` promotion workflow is explicit and auditable
4. Portfolio and run history cover backtest, paper, and live-adjacent results through one consistent model
5. Backfill checkpoint reliability is validated across providers and date ranges

---

## Reference Documents

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md)
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md)
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md)
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
