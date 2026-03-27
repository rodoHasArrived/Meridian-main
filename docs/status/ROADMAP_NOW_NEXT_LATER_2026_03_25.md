# Meridian — Now / Next / Later Roadmap

**Generated:** 2026-03-26
**Format:** Now / Next / Later
**Basis:** Wave 1–5 roadmap + FEATURE_INVENTORY.md + codebase audit
**Change from prior roadmap:** Adds Security Master productization as a **Next** initiative; reorganises delivery waves into horizon buckets; retains all existing wave goals; marks the WPF workstation as an active Windows delivery track again as of March 26, 2026.

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

### 3. Native Desktop Workstation Refresh *(active again as of 2026-03-26)*

The WPF workstation is no longer parked as a passive maintenance surface. Native desktop development has restarted around a Windows-first operator shell that complements the web dashboard for research, live monitoring, provider operations, and governance workflows.

**Delivery scope:**

- Modernize the shell and navigation model in `src/Meridian.Wpf`
- Redesign high-traffic workflow pages such as Live Data, Provider, Backfill, and Data Quality for clearer operator use
- Reduce dense page chrome and improve workstation hierarchy, page context, and recent-task movement
- Continue MVVM extraction where pages still depend heavily on code-behind orchestration

**Exit gate:** The desktop workstation feels coherent and actively maintained across its core operator pages, not just present in the solution.

---

## Next

### 4. Security Master Productization *(new initiative)*

**Background and rationale:**

The Security Master backend is complete: contracts (`SecurityCommands`, `SecurityDtos`, `SecurityEvents`), services (`SecurityMasterService`, `SecurityMasterQueryService`, `SecurityMasterProjectionService`), PostgreSQL storage, migration runner, F# domain modules (`SecurityMaster.fs`, `SecurityClassification.fs`, `SecurityEconomicDefinition.fs`), and snapshot/event stores are all present in the codebase.

The gap is the operator layer. Today, Security Master data is invisible to an operator running the platform. There is no way to search, browse, classify, or reconcile security definitions without writing code. This limits the platform's value for the governance and data lineage use cases that Security Master exists to serve.

**Delivery scope:**

- Security search panel in the web dashboard: search by ticker, FIGI, CUSIP, ISIN; display classification, economic definition, identifiers, provider mapping
- Classification browser: view and navigate the security classification hierarchy
- Economic definition editor: view and edit economic definition fields (coupon, maturity, strike, put/call, etc.) for supported asset classes
- Cross-provider reconciliation surface: surface provider mapping conflicts and gaps
- Provider coverage metrics: which symbols have Security Master entries vs. which are streaming without backing definitions
- Workstation REST endpoints for security search and detail (extend `SecurityMasterEndpoints.cs` with workstation-facing read models)

**Dependencies:**

- Stable provider registry (Now → 1) ensures Security Master entries can be cross-referenced against active providers
- Web dashboard cockpit infrastructure (Now → 2) establishes the component and layout patterns these panels follow

**Exit gate:** An operator can search for a security, inspect its classification and economic definition, and see which providers have conflicting or missing mappings — entirely through the web dashboard with no code required.

---

### 5. Portfolio and Strategy Tracking Depth *(Wave 3)*

Strengthen the portfolio read models and multi-run comparison so strategy research produces durable, comparable results.

**Delivery scope:**

- Portfolio drill-ins: attribution by symbol, drawdown breakdown, trade-level analysis
- Multi-run strategy comparison: overlay performance curves, compare metrics side-by-side
- Run history covering paper and live-adjacent results, not only backtest runs
- Ledger reconciliation surface in the web dashboard

**Exit gate:** Portfolio and strategy tracking are useful for iterative strategy development across multiple runs, not just single-run review.

---

### 6. Backtest Studio Unification *(Wave 4)*

Consolidate the native and Lean backtest experiences into one coherent workflow. The native engine and QuantConnect Lean integration are both operational; the gap is a unified UI and result model.

**Delivery scope:**

- Unified Backtest Studio spanning both engines: single entry point, consistent result model
- Strategy comparison and run-diff tooling
- Broader fill model coverage: partial fills, slippage, market impact
- Backtest performance improvements for large historical windows

**Exit gate:** Backtesting feels like one product whether using the native engine or Lean, with consistent result models and a shared comparison workflow.

---

## Later

### 7. Live Integration Readiness *(Wave 5)*

The brokerage gateway framework (`IBrokerageGateway`, `BaseBrokerageGateway`) and provider-specific adapters (Alpaca, IB, StockSharp) are implemented. The remaining work is validating these adapters against live vendor surfaces and adding execution audit trail.

**Delivery scope:**

- Validate brokerage gateway adapters against real vendor APIs (Alpaca, IB, StockSharp)
- Execution audit trail sufficient for live operations
- Operator controls: circuit breakers, position limits, manual overrides
- `Paper → Live` promotion gate using the existing brokerage gateway framework

**Prerequisite:** Paper trading cockpit (Now → 2) must be complete and stable before live integration work begins. The gateway without a cockpit leaves operators with no visibility into live order state.

**Exit gate:** At least one brokerage adapter is validated against a live vendor surface, with full audit trail and operator controls in place.

---

### 8. Advanced Research and Scale *(Optional Wave)*

Depth multipliers that require a stable platform foundation to deliver value. None of these are prerequisites for core operator value.

- QuantScript runtime and editor
- L3 inference and queue-aware execution simulation
- Multi-instance collector coordination and horizontal scale-out
- Phase 16 assembly-level performance optimizations
- Broader WPF workstation coverage after the current active shell/page refresh stabilizes
- WPF desktop app (code in `src/Meridian.Wpf/` - included in solution build and back in active development)

---

## Platform Release Gates

Meridian can claim core-platform readiness when all of the following are true:

1. Every major provider has documented replay/runtime validation evidence
2. Paper trading is exposed as a full cockpit in the web dashboard
3. The `Backtest → Paper` promotion workflow is explicit and auditable
4. Portfolio and run history cover backtest, paper, and live-adjacent results through one consistent model
5. Backfill checkpoint reliability is validated across providers and date ranges
6. Security Master is operator-accessible via the web dashboard for search, classification, and provider reconciliation

---

## Risks

**Provider trust is a gating dependency.** Meridian should not overclaim operator readiness where replay/runtime evidence is still thin.

**Paper trading cockpit must precede Paper → Live.** The gateway without a cockpit is incomplete operator tooling. Live integration work is blocked on cockpit completion.

**Backfill reliability directly affects backtest quality.** Data gaps silently corrupt results — checkpoint and gap-detection hardening is not optional.

**Storage-path shutdown safety is part of data trust.** A flush failure in `ParquetStorageSink` undermines the same operator confidence that provider validation is supposed to establish.

**NYSE cancellation gaps remain active reliability debt.** Transport paths that ignore caller cancellation can weaken graceful shutdown and reconnect behavior.

**Security Master productization competes for dashboard resources.** The cockpit and Security Master panels share the same React dashboard. Sequencing them (cockpit first) avoids UI layout conflicts and lets Security Master follow established component patterns.

**Test coverage must grow with the platform.** Strategy correctness, fill model edge cases, and provider adapters need explicit regression coverage before live integration.

---

## Reference Documents

- [`ROADMAP.md`](ROADMAP.md) — Prior wave-structured roadmap (source for this reorganisation)
- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) — Current-vs-target capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) — Normalised backlog
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
