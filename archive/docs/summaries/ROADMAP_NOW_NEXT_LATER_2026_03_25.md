# Meridian — Now / Next / Later Roadmap

**Generated:** 2026-04-03
**Format:** Now / Next / Later
**Basis:** ROADMAP.md + FEATURE_INVENTORY.md + production-status.md + workstation/governance blueprints (2026-04-03)
**Change from prior update (2026-03-31):** Marks WPF workstation shell modernization items as complete (Fluent theme, SVG icons, candlestick charting, zero-API-key startup, workflow guide, screenshot CI). Adds Phase 1.5 preferred/convertible equity domain extension to the Later optional wave.

---

## How to Read This

| Horizon | Meaning |
|---------|---------|
| **Now** | Active delivery — in flight, blocking operator value |
| **Next** | Committed and sequenced — starts when Now items clear their exit gates |
| **Later** | Directionally committed — not yet scheduled; requires stable platform foundation |

---

## Now

### 1. Provider Reliability and Data Confidence *(Wave 1 — active)*

The platform's downstream value depends on trustworthy data. Operator confidence in backtesting and execution depends on provder correctness — nothing in Next ships with confidence until this gate closes.

**Delivery scope:**

- Polygon replay coverage across feeds and edge cases
- IB runtime/bootstrap validation against real vendor surfaces
- NYSE shared-lifecycle depth coverage
- StockSharp connector examples and validated adapters
- Backfill checkpoint reliability and gap detection across providers and date ranges
- Parquet sink flush-path hardening and ADR-014 cleanup for L2 snapshot persistence
- NYSE transport hardening: `HttpClientFactory` alignment and cancellation-safe websocket send/resubscribe flows

**Exit gate:** Every major provider has documented replay/runtime evidence and passes its validation suite.

---

### 2. Paper Trading Cockpit *(Wave 2 — in progress)*

The paper trading gateway and brokerage adapter framework are implemented. The gap is making them visible and usable through the web dashboard with a clear path from backtest to paper. The REST endpoints were wired in March 2026; the remaining work is the dashboard UI surfaces.

**Delivery scope:**

- Web dashboard: live positions, open orders, fills, P&L, risk state panels — wired to brokerage gateways
- `Backtest → Paper` promotion: explicit lifecycle step, audit trail, safety gate
- Paper session persistence and replay
- Brokerage gateway cockpit integration (Alpaca, IB, StockSharp adapters are implemented)

**Exit gate:** A strategy can be researched in backtest and promoted to paper trading through one connected workflow in the web dashboard.

---

### 3. Native Desktop Workstation Refresh *(active — shell modernization complete, page-level work ongoing)*

The WPF workstation shell has been substantially modernized. The focus now shifts to page-level workflow redesign and MVVM extraction.

**Completed (2026-04-03):**

- ✅ Native Fluent theme via `ThemeMode="System"` in `App.xaml` (PR #524)
- ✅ SVG icon set — Segoe MDL2 Assets glyphs replacing emoji icons across all pages (PR #512)
- ✅ Candlestick charting — LiveCharts2 candlestick chart on Charting page (PR #522)
- ✅ Zero-API-key startup — Synthetic provider default when no credentials are present (PR #513)
- ✅ Workflow guide (`docs/WORKFLOW_GUIDE.md`) with live UI screenshots (PR #511)
- ✅ CI: Refresh UI Screenshots GitHub Action (PR #515)
- ✅ Route and health endpoint reliability — duplicate DFA route definitions and duplicate health endpoint registrations resolved (PRs #521, #519)

**Remaining open scope:**

- Redesign high-traffic workflow pages: Live Data, Provider, Backfill, and Data Quality for clearer operator use
- Continue MVVM extraction where pages still depend heavily on code-behind orchestration

**Exit gate:** The desktop workstation feels coherent and actively maintained across its core operator pages, not just present in the solution.

---

## Next

### 4. Security Master Productization *(new initiative)*

**Background and rationale:**

The Security Master platform baseline is already present in code: contracts, query/services, PostgreSQL storage, F# domain modules, corporate actions, trading parameters, bulk ingest, conflict resolution endpoints, and a WPF browser. The remaining gap is the shared operator layer.

Today, Security Master is still underrepresented in the web dashboard and broader workstation workflows. The next step is not "build Security Master" but "make Security Master visible and useful across portfolio, ledger, reconciliation, and governance flows."

**Delivery scope:**

- Security search panel in the web dashboard: search by ticker, FIGI, CUSIP, ISIN; display classification, economic definition, identifiers, provider mapping
- Classification browser: view and navigate the security classification hierarchy
- Economic definition editor: view and edit economic definition fields (coupon, maturity, strike, put/call, etc.) for supported asset classes
- Cross-provider reconciliation surface: surface provider mapping conflicts and gaps
- Provider coverage metrics: which symbols have Security Master entries vs. which are streaming without backing definitions
- Workstation-facing enrichment so portfolio, ledger, and reconciliation DTOs consume one authoritative instrument layer

**Dependencies:**

- Stable provider registry (Now → 1) ensures Security Master entries can be cross-referenced against active providers
- Web dashboard cockpit infrastructure (Now → 2) establishes the component and layout patterns these panels follow

**Exit gate:** An operator can search for a security, inspect its classification and economic definition, and see which providers have conflicting or missing mappings through the dashboard and shared workstation flows with no code required.

---

### 5. Governance and Fund-Operations Foundation

The active planning set now treats governance as a connected delivery track rather than a distant optional add-on. Shared run, portfolio, ledger, reconciliation, direct-lending, and export seams already exist; the gap is turning them into explicit operator workflows.

**Delivery scope:**

- Multi-ledger grouping, selection, and consolidated trial-balance read paths
- Cash-flow modeling and realized-vs-projected governance views
- Reconciliation expansion from run-scoped history into explicit break queues and operator review flows
- Governance quick actions and report-pack entry points built on the existing export stack

**Exit gate:** Governance is represented by concrete operator workflows and shared DTOs, not only blueprints and backend services.

---

### 6. Portfolio and Strategy Tracking Depth *(Wave 3)*

Strengthen the portfolio read models and multi-run comparison so strategy research produces durable, comparable results.

**Delivery scope:**

- Portfolio drill-ins: attribution by symbol, drawdown breakdown, trade-level analysis
- Multi-run strategy comparison: overlay performance curves, compare metrics side-by-side
- Run history covering paper and live-adjacent results, not only backtest runs
- Ledger reconciliation surface in the web dashboard

**Exit gate:** Portfolio and strategy tracking are useful for iterative strategy development across multiple runs, not just single-run review.

---

### 7. Backtest Studio Unification *(Wave 4)*

Consolidate the native and Lean backtest experiences into one coherent workflow. The native engine and QuantConnect Lean integration are both operational; the gap is a unified UI and result model.

**Delivery scope:**

- Unified Backtest Studio spanning both engines: single entry point, consistent result model
- Strategy comparison and run-diff tooling
- Broader fill model coverage: partial fills, slippage, market impact
- Backtest performance improvements for large historical windows

**Exit gate:** Backtesting feels like one product whether using the native engine or Lean, with consistent result models and a shared comparison workflow.

---

## Later

### 7. QuantScript Research Environment *(Wave 7 — new initiative)*

**Background and rationale:**

The QuantScript backend is already built: `RoslynScriptCompiler`, `ScriptRunner`, `QuantDataContext`, `BacktestProxy`, `DataProxy`, `StatisticsEngine`, `PortfolioBuilder`, `PriceSeries`, `TechnicalSeriesExtensions`, `EfficientFrontierConstraints`, `PlotQueue`, and `ScriptParamAttribute` are all present in `src/Meridian.QuantScript/`. The `QuantScriptPage` and `QuantScriptViewModel` exist in the WPF workstation. The gap is an integrated, operator-ready research surface connected to the live platform and native backtest engine.

**Delivery scope:**

- Polish the QuantScript editor surface (`QuantScriptPage`) in the WPF workstation: code editing, parameter binding, and plot output rendering via `PlotRenderBehavior`
- Wire `QuantDataContext` to live historical and streaming data so scripts consume real platform data
- Expose `BacktestProxy` from within scripts so iterative strategy exploration uses the native backtest engine directly
- Script result export (JSONL/CSV/chart image) through the analysis export pipeline
- Script library management: save, load, and version user scripts
- Introductory example scripts demonstrating common research patterns (momentum, pairs, mean-reversion)

**Exit gate:** An operator can write a research script in the QuantScript editor, run it against real platform data, and export or compare results — without leaving the workstation.

---

### 8. Operator Observability Platform *(Wave 8 — new initiative)*

**Background and rationale:**

The monitoring backend is rich but its outputs are not surfaced to operators through a unified interface. `DataQualityMonitoringService`, `DataFreshnessSlaMonitor`, `AnomalyDetector`, `CompletenessScoreCalculator`, `CrossProviderComparisonService`, `GapAnalyzer`, `PrometheusMetrics`, `ProviderDegradationScorer`, `ProviderLatencyService`, `SpreadMonitor`, `SequenceErrorTracker`, and `AlertDispatcher` are all present in `src/Meridian.Application/Monitoring/`. Operators currently must inspect logs or wire custom queries to see this data. A unified dashboard surface makes the platform's health observable at a glance.

**Delivery scope:**

- Unified health dashboard in the web dashboard: provider status, SLA compliance, data quality scores, and alert state on one screen
- SLA enforcement reporting: configurable thresholds, breach timelines, and provider-level accountability summaries
- Alert management UI: view active alerts, acknowledge, configure suppression windows, and review alert history
- Cross-provider comparison surface: symbol-level data completeness side-by-side across providers
- Historical data quality trend charts: completeness scores, gap frequency, and latency histograms over time
- Prometheus scrape endpoint documentation and a reference Grafana dashboard JSON shipped with the deployment assets

**Exit gate:** An operator can open the observability dashboard, see the current health state of all providers and data streams, drill into an SLA breach, and configure alert thresholds — without inspecting logs or writing queries.

---

### 9. Live Integration Readiness *(Wave 5)*

The brokerage gateway framework (`IBrokerageGateway`, `BaseBrokerageGateway`) and provider-specific adapters (Alpaca, IB, StockSharp) are implemented. The remaining work is validating these adapters against live vendor surfaces and adding execution audit trail.

**Delivery scope:**

- Validate brokerage gateway adapters against real vendor APIs (Alpaca, IB, StockSharp)
- Execution audit trail sufficient for live operations
- Operator controls: circuit breakers, position limits, manual overrides
- `Paper → Live` promotion gate using the existing brokerage gateway framework

**Prerequisite:** Paper trading cockpit (Now → 2) must be complete and stable before live integration work begins. The gateway without a cockpit leaves operators with no visibility into live order state.

**Exit gate:** At least one brokerage adapter is validated against a live vendor surface, with full audit trail and operator controls in place.

---

### 10. Advanced Research and Scale *(Optional Wave)*

Depth multipliers that require a stable platform foundation to deliver value. None of these are prerequisites for core operator value.

- L3 inference and queue-aware execution simulation
- Multi-instance collector coordination and horizontal scale-out
- Phase 16 assembly-level performance optimizations
- Broader WPF workstation coverage after the current active shell/page refresh stabilizes
- WPF desktop app (code in `src/Meridian.Wpf/` - included in solution build and back in active development)
- **Phase 1.5 — Preferred & Convertible Equity domain extension:** add `EquityClassification` discriminated union, `PreferredTerms`, `ConvertibleTerms`, and `LiquidationPreference` types to `src/Meridian.FSharp/Domain/SecurityMaster.fs`; update `EquityTerms` to include optional `Classification` field; add unit tests and update `CLAUDE.domain-naming.md`. *(Issue: `issues/phase_1_5_1_add_equityclassification_discriminator_and_preferredterms_domain_model.md` — all acceptance criteria open)*

---

## Platform Release Gates

Meridian can claim core-platform readiness when all of the following are true:

1. Every major provider has documented replay/runtime validation evidence
2. Paper trading is exposed as a full cockpit in the web dashboard
3. The `Backtest → Paper` promotion workflow is explicit and auditable
4. Portfolio and run history cover backtest, paper, and live-adjacent results through one consistent model
5. Backfill checkpoint reliability is validated across providers and date ranges
6. Security Master is operator-accessible via the web dashboard for search, classification, and provider reconciliation
7. Platform health is observable via the Operator Observability Dashboard without requiring log inspection

---

## Risks

**Provider trust is a gating dependency.** Meridian should not overclaim operator readiness where replay/runtime evidence is still thin.

**Paper trading cockpit must precede Paper → Live.** The gateway without a cockpit is incomplete operator tooling. Live integration work is blocked on cockpit completion.

**Backfill reliability directly affects backtest quality.** Data gaps silently corrupt results — checkpoint and gap-detection hardening is not optional.

**Storage-path shutdown safety is part of data trust.** A flush failure in `ParquetStorageSink` undermines the same operator confidence that provider validation is supposed to establish.

**NYSE cancellation gaps remain active reliability debt.** Transport paths that ignore caller cancellation can weaken graceful shutdown and reconnect behavior.

**Security Master productization competes for dashboard resources.** The cockpit and Security Master panels share the same React dashboard. Sequencing them (cockpit first) avoids UI layout conflicts and lets Security Master follow established component patterns.

**QuantScript editor depends on stable backtest engine.** The `BacktestProxy` integration inside QuantScript scripts requires a stable and well-tested native engine; Wave 4 (Backtest Studio) should complete before QuantScript goes into final polish.

**Test coverage must grow with the platform.** Strategy correctness, fill model edge cases, and provider adapters need explicit regression coverage before live integration.

---

## Reference Documents

- [`ROADMAP.md`](ROADMAP.md) — Prior wave-structured roadmap (source for this reorganisation)
- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) — Current-vs-target capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) — Normalised backlog
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
