# Meridian - Project Roadmap

**Last Updated:** 2026-03-26
**Status:** Refocused on core platform functionality and active native desktop redevelopment
**Repository Snapshot (2026-03-24):** solution projects: 35 | `src/` projects: 27 | test projects: 7 | workflow files: 35 | source files: 1,118 (1,073 C# + 45 F#) | test files: 335 (326 C# + 9 F#) | tests: ~4,424

Meridian is a self-hosted trading platform. The active delivery focus is the four core platform pillars: **data collection**, **backtesting**, **real-time execution**, and **portfolio/strategy tracking**. The web dashboard remains the primary cross-platform operator surface. As of **March 26, 2026**, the WPF desktop app is back in **active development** as the Windows-first workstation track, beginning with shell modernization and workflow-page redesigns in `src/Meridian.Wpf/`.

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
- Fix storage-path reliability gaps in the Parquet sink (flush lifecycle, ADR-014 serialization cleanup, shutdown safety)
- Finish NYSE transport hardening: factory-managed HTTP clients plus cancellation propagation through resubscribe/send paths
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
- Pre-trade risk rules via `IRiskRule` (position limits, drawdown circuit breaker, order rate throttle)
- Position and fill tracking
- **Brokerage gateway framework** (`BaseBrokerageGateway`, `BrokerageGatewayAdapter`, `IBrokerageGateway`) with provider-specific implementations:
  - `AlpacaBrokerageGateway` — Alpaca order routing with fractional quantity support
  - `IBBrokerageGateway` — Interactive Brokers order routing (conditional on IBAPI build flag)
  - `StockSharpBrokerageGateway` — StockSharp connector-based order routing
  - `TemplateBrokerageGateway` — scaffold for new brokerage adapters
- Brokerage DI registration via `BrokerageServiceRegistration` and `BrokerageConfiguration`

**Completed (2026-03-25):**
- Paper-trading cockpit REST endpoints wired: `/api/execution/account`, `/api/execution/positions`, `/api/execution/portfolio`, `/api/execution/orders`, `/api/execution/health`, `/api/execution/capabilities`
- Paper-trading session management endpoints: `/api/execution/sessions` (create, list, detail, close)
- `Backtest → Paper → Live` promotion workflow: `/api/promotion/evaluate/{runId}`, `/api/promotion/approve`, `/api/promotion/reject`, `/api/promotion/history`
- Strategy lifecycle control endpoints: `/api/strategies/status`, `/api/strategies/{id}/status`, `/api/strategies/{id}/pause`, `/api/strategies/{id}/stop`
- `PaperSessionPersistenceService`, `IPortfolioState`, `IOrderGateway`, `IOrderManager`, `StrategyLifecycleManager` fully wired in DI
- Test coverage added for `PromotionService` and `PaperSessionPersistenceService`

**Remaining work:**
- Wire brokerage gateways into live order routing (currently paper-only)
- Define the `Paper → Live` promotion gate with additional human-approval controls
- Add paper-trading session replay from persisted order history
- Improve paper-trading cockpit UI in the React dashboard

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
- **Brokerage gateway framework** with Alpaca, IB, and StockSharp adapters
- Strategy SDK, lifecycle management, and portfolio tracking
- Ledger infrastructure and double-entry accounting foundation
- Direct lending module (PostgreSQL-backed services, workflows, API endpoints)
- Security Master foundations (contracts, services, storage, F# domain)
- Symbol search across 5 providers
- Web dashboard serving all core workflows via REST API (300 route constants, 0 stubs)
- Provider registration, DI composition, route coverage, and observability baseline
- Deployment assets (Docker, k8s, systemd)
- CppTrader integration (host management, order gateway, replay, ITCH ingestion)

### Partial

- Provider confidence: Polygon replay breadth, IB runtime, NYSE shared-lifecycle, and StockSharp breadth need validation depth
- Backfill checkpoint reliability across longer runs and provider-specific edge cases
- Parquet persistence is feature-complete but still needs shutdown-path and serialization hardening in `ParquetStorageSink`
- Paper trading cockpit surfaces in the web UI (gateway and brokerage adapters are implemented; dashboard exposure is incomplete)
- `Backtest → Paper` promotion workflow (read services exist; explicit lifecycle flow is not yet wired)
- Portfolio drill-ins and multi-run comparison depth
- Ledger reconciliation exposed through the web dashboard
- Security Master productization (code foundations exist; Wave 6 delivers operator-facing surfaces — see [`docs/plans/security-master-productization-roadmap.md`](../plans/security-master-productization-roadmap.md))
- Brokerage gateway live-order integration (adapters exist; live-validated runtime paths pending)
- WPF desktop redevelopment is active again: the shell refresh is in flight and operator pages such as `MainPage`, `LiveDataViewerPage`, and `ProviderPage` are being modernized, but many workflow pages still retain significant code-behind orchestration and direct UI state sync

### Planned

- Full paper-trading cockpit via the web dashboard (wiring brokerage gateways into cockpit panels)
- `Backtest → Paper → Live` promotion workflow with audit trail
- Unified Backtest Studio across native and Lean engines
- Strategy comparison and run-diff tooling
- Live broker integration validation (brokerage gateway framework is in place; live-validated runtime paths remain)
- QuantScript Research Environment: polish the editor surface in the WPF workstation and wire it to live platform data and the native backtest engine
- Operator Observability Platform: unified health dashboard, SLA enforcement reporting, alert management UI, and cross-provider data quality surface

### Optional / Later

- L3 inference and queue-aware execution simulation
- Governance: multi-ledger, trial balance, cash-flow, report packs
- Multi-instance collector coordination and horizontal scale-out
- Phase 16 assembly-level performance optimizations
- Deeper WPF workstation coverage should continue after the current shell/page modernization wave and further MVVM extraction in workflow pages such as `BackfillPage`
- WPF desktop app (code in `src/Meridian.Wpf/` - included in solution build, now actively redeveloping as the Windows-first workstation surface)

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
- Parquet sink flush-path hardening and ADR-014 cleanup for L2 snapshot persistence
- NYSE adapter transport hardening: `HttpClientFactory` alignment and cancellation-safe websocket send/resubscribe flows

**Exit signal:** Every major provider has documented replay/runtime evidence and passes its validation suite.

---

### Wave 2: Paper trading cockpit and promotion workflow

The paper trading gateway and brokerage adapter framework both exist. The gap is making them visible and usable through the web dashboard with a clear path from backtest to paper.

**Focus:**
- Web dashboard: live positions, open orders, fills, P&L, risk state panels — wired to brokerage gateways
- `Backtest → Paper` promotion: explicit lifecycle step, audit trail, safety gate
- Paper session persistence and replay
- Brokerage gateway integration into cockpit panels (Alpaca, IB, StockSharp adapters are implemented)

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

The brokerage gateway framework (`IBrokerageGateway`, `BaseBrokerageGateway`) and provider-specific adapters (Alpaca, IB, StockSharp) are now implemented. The remaining work is validating these adapters against live vendor surfaces and adding execution audit trail.

**Focus:**
- Validate brokerage gateway adapters against real vendor APIs (Alpaca, IB, StockSharp)
- Add execution audit trail sufficient for live operations
- Define operator controls (circuit breakers, position limits, manual overrides)
- Wire `Paper → Live` promotion gate using the existing brokerage gateway framework

**Exit signal:** At least one brokerage adapter is validated against a live vendor surface with audit trail.

---

### Wave 6: Security Master productization

Security Master has contracts, Postgres storage, F# domain models, and REST endpoints. Wave 6 turns it into an operator-visible platform layer that serves as the authoritative instrument-definition source across research, portfolio, governance, and ledger workflows.

**Focus:**
- Bond term richness: coupon, maturity, day-count, seniority fields in `SecurityEconomicDefinition`
- Trading parameters: lot size, tick size, contract multiplier, margin requirement per instrument
- Corporate action events: dividend, split, spin-off, merger events with backtest adjustment integration
- Exchange bulk ingest: CSV and provider (Polygon) bulk-ingest path with idempotent dedup
- Golden record conflict resolution: detect, store, and resolve field-level conflicts from multiple providers
- WPF Security Master browser: `SecurityMasterPage` + `SecurityMasterViewModel` in the desktop app

Full design: [`docs/plans/security-master-productization-roadmap.md`](../plans/security-master-productization-roadmap.md)

**Exit signal:** Security Master is searchable in the web dashboard and WPF app, corporate actions adjust backtest prices, and multi-provider conflicts surface with a resolution workflow.

---

### Wave 7: QuantScript Research Environment

The QuantScript backend is complete: `RoslynScriptCompiler`, `ScriptRunner`, `QuantDataContext`, `BacktestProxy`, `DataProxy`, `StatisticsEngine`, `PortfolioBuilder`, `PriceSeries`, `TechnicalSeriesExtensions`, `EfficientFrontierConstraints`, `PlotQueue`, and `ScriptParamAttribute` are all in place. The `QuantScriptPage` and `QuantScriptViewModel` exist in the WPF workstation. The gap is an integrated, operator-ready research surface connected to the live platform.

**Focus:**
- Polish the QuantScript editor surface (`QuantScriptPage`) in the WPF workstation: code editing, parameter binding, plot output rendering via `PlotRenderBehavior`
- Wire `QuantDataContext` to live historical and streaming data so scripts consume real platform data
- Expose `BacktestProxy` from within scripts so iterative strategy exploration uses the native backtest engine
- Script result export (JSONL/CSV/chart image) via the analysis export pipeline
- Script library management: save, load, and version user scripts
- Introductory example scripts demonstrating common research patterns (momentum, pairs, mean-reversion)

**Exit signal:** An operator can write a research script in the QuantScript editor, run it against real platform data, and export or compare results — without leaving the workstation.

---

### Wave 8: Operator Observability Platform

The monitoring backend is rich but fragmented. `DataQualityMonitoringService`, `DataFreshnessSlaMonitor`, `AnomalyDetector`, `CompletenessScoreCalculator`, `CrossProviderComparisonService`, `GapAnalyzer`, `PrometheusMetrics`, `ProviderDegradationScorer`, `ProviderLatencyService`, `SpreadMonitor`, `SequenceErrorTracker`, and `AlertDispatcher` are all implemented. The gap is a unified, navigable operator surface that makes this monitoring data actionable in real time.

**Focus:**
- Unified health dashboard in the web dashboard: provider status, SLA compliance, data quality scores, and alert state — all on one screen
- SLA enforcement reporting: configurable thresholds, breach timelines, and provider-level accountability summaries
- Alert management UI: view active alerts, acknowledge, configure suppression windows, and review alert history
- Cross-provider comparison surface: symbol-level data completeness side-by-side across providers
- Historical data quality trend charts: completeness scores, gap frequency, and latency histograms over time
- Prometheus metrics integration guide: documented scrape endpoint and a reference Grafana dashboard JSON

**Exit signal:** An operator can open the observability dashboard, see the current health state of all providers and data streams, drill into an SLA breach, and configure alert thresholds — without inspecting logs or writing queries.

---

### Optional Wave: Advanced research and scale

Depth multipliers that require a stable platform foundation to deliver value.

**Focus:**
- L3 inference and queue-aware simulation
- Multi-instance collector scale-out
- Phase 16 assembly-level performance work

**Exit signal:** These tracks deepen the platform; they are not prerequisites for core operator value.

---

## Risks

- **Provider trust is a gating dependency.** Meridian should not overclaim operator readiness where replay/runtime evidence is still thin.
- **Paper trading cockpit must be wired before Paper → Live work starts.** The gateway without a cockpit is incomplete operator tooling.
- **Backfill reliability directly affects backtest quality.** Data gaps silently corrupt results — checkpoint and gap-detection hardening is not optional.
- **Storage-path shutdown safety is part of data trust.** A flush failure in `ParquetStorageSink` weakens the same operator confidence that provider validation is meant to build.
- **NYSE cancellation gaps weaken graceful shutdown and reconnect behavior.** Transport paths that ignore caller cancellation should be treated as active Wave 1 reliability debt.
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
