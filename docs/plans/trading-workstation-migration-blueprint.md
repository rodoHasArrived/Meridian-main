# Trading Workstation Migration Blueprint

**Owner:** Core Team
**Audience:** Product, Architecture, Desktop, API, and Platform contributors
**Last Updated:** 2026-04-25
**Status:** Active blueprint — WPF shell/navigation baseline is implemented; workflow validation and cockpit/shared-model/governance hardening remain in progress

---

## 1. Purpose

This blueprint defines the migration from Meridian's current feature-rich but page-centric desktop UX to a **workflow-centric trading workstation**.

The desired end state is a product where **research, backtesting, paper trading, future live trading, portfolio analysis, and ledger auditability** are experienced as one continuous workflow rather than several adjacent tools.

This workstation blueprint is now paired with [governance-fund-ops-blueprint.md](governance-fund-ops-blueprint.md). Together they define the intended Meridian end state: one connected fund-management workflow that spans research, strategy implementation, trade management, portfolio, ledger, Security Master, reconciliation, cash-flow, and reporting.

This plan does **not** replace Meridian's existing platform pillars. Instead, it reorganizes them around a unified operator model:

- **Strategy** — what is being run
- **Run** — a single backtest, paper, or live execution session
- **Portfolio** — the evolving state produced by the run
- **Ledger** — the auditable accounting trail for the portfolio
- **Dataset / Feed** — the market data source powering the run
- **Workspace** — the operator experience for managing the lifecycle

---

## 2. Why This Migration Exists

Meridian already contains strong underlying capabilities:

- tick-level backtesting
- paper-trading execution primitives
- strategy lifecycle tracking
- data-quality-aware ingestion and replay
- a double-entry ledger implementation
- a broad WPF page inventory and supporting UI services

However, those capabilities are still exposed through multiple page- and service-centric flows. The WPF shell now has a four-workspace baseline, metadata-driven navigation, command/search metadata, shared deep-page hosting, context strips, and smoke coverage, but the product still needs to prove that the active workflows are better, not just that the shell is more organized.

### Current pain points

1. **Backtesting is still split across multiple experiences**
   - Native WPF backtest page
   - Lean integration page
   - no unified run browser or comparison workflow

2. **Paper trading is still infrastructure-first, operator-second**
   - OMS and paper gateway exist
   - trading cockpit, positions, blotter, and risk surfaces exist, but daily-use acceptance still depends on DK1 trust evidence, promotion rationale, replay/session reliability, and operator sign-off

3. **Ledger capability is still not consistently first-class**
   - accounting exists in the engine
   - users have run-centered ledger, trial-balance, and reconciliation seams, but broader account/entity, cash-flow, multi-ledger, and governed-output workflows remain incomplete

4. **The UI is still page-dense below the shell**
   - many pages are individually useful
   - the shell baseline is in place, but end-to-end workflows still need acceptance evidence across active Wave 2-4 paths

---

## 3. Migration Goals

### Primary goals

- Make **Strategy Run** the central product object across backtest, paper, and live modes.
- Promote **Portfolio + Ledger** from internal implementation detail to first-class user experience.
- Consolidate desktop navigation into **workflow workspaces**.
- Unify native and Lean backtesting under a single operator-facing model.
- Evolve paper trading into a realistic pre-live operating environment.

### Non-goals

- Rewriting core ingestion, storage, or provider abstractions.
- Replacing the WPF shell in this migration.
- Removing Lean integration.
- Introducing real-money broker routing by default.

---

## 4. Target Product Information Architecture

The target UX is organized around four top-level workspaces.

## 4.1 Research

**Purpose:** Explore data, validate coverage, run experiments, compare results.

**Consolidates / fronts:**
- BacktestPage
- LeanIntegrationPage (backtest functions)
- ChartingPage
- EventReplayPage
- DataCalendarPage
- AdvancedAnalyticsPage
- AnalysisExport pages when used as research output

**Primary tasks:**
- choose dataset
- validate coverage / data quality
- run backtests
- compare multiple runs
- inspect fills and attribution
- export research outputs

## 4.2 Trading

**Purpose:** Operate strategies in paper mode now, live mode later, with clear risk and audit controls.

**Consolidates / fronts:**
- LiveDataViewerPage
- OrderBookPage
- future Orders / Positions / Portfolio / Strategy Runs pages
- strategy lifecycle controls
- execution audit panels

**Primary tasks:**
- monitor active strategies
- review orders and fills
- inspect positions, exposure, and P&L
- pause / stop / flatten safely
- promote strategy configuration from backtest to paper to live

## 4.3 Data Operations

**Purpose:** Manage providers, symbols, backfills, storage, and export operations.

**Consolidates / fronts:**
- BackfillPage
- SymbolsPage
- SymbolMappingPage
- SymbolStoragePage
- StoragePage
- ScheduleManagerPage
- Provider pages
- PackageManagerPage
- export and packaging flows when used operationally

## 4.4 Governance

**Purpose:** Risk, ledger, diagnostics, audit trail, notifications, and settings.

**Consolidates / fronts:**
- portfolio ledger views
- diagnostics
- notifications
- retention / archival assurance
- settings
- credential / integration safety surfaces

---

## 5. Target Domain and Application Model

## 5.1 Shared Run model

Introduce or standardize on a single application-level run model:

```text
StrategyRun
- RunId
- StrategyId
- Mode: Backtest | Paper | Live
- Engine: MeridianNative | Lean | BrokerPaper | BrokerLive
- DatasetReference / FeedReference
- PortfolioId
- ParameterSet
- Status
- StartedAt / CompletedAt
- MetricsSnapshot
- LedgerReference
- AuditReference
```

This model should be queryable from both WPF and the retained desktop-local API surfaces.

## 5.2 Shared portfolio model

Standardize a read model for:

- cash
- gross / net exposure
- long / short market value
- realized / unrealized P&L
- financing costs
- commissions
- per-symbol attribution
- daily equity series

## 5.3 Ledger as first-class read model

Create explicit reporting/read services for:

- journal entries
- trial balance
- account summaries
- per-symbol subledger views
- financing / commission summaries
- equity-change attribution

## 5.4 Application orchestration layer

Add workflow-level orchestration services rather than expanding page-level service wrappers:

- `BacktestRunOrchestrator`
- `TradingRunOrchestrator`
- `PortfolioReadService`
- `LedgerReadService`
- `RunComparisonService`
- `PromotionWorkflowService`

These services should sit above raw engine/service primitives and below UI view models.

---

## 6. Target UI Surfaces

## 6.1 Research workspace

**Default layout**
- Left: strategy, engine, dataset, parameters
- Center: equity curve / charts / progress
- Right: metrics, fills, attribution, ledger drill-ins
- Bottom or tabbed detail: run comparison and event log

## 6.2 Trading cockpit

**Default layout**
- Left: active strategies + watchlists
- Center: market view, positions, and action panels
- Right: order blotter, fills, risk, alerts
- Optional lower panel: ledger / audit event stream

**Required operator controls**
- pause strategy
- stop strategy
- cancel all open orders
- flatten positions
- acknowledge risk alerts

## 6.3 Portfolio & Ledger workspace

**Primary tabs**
- Overview
- Positions
- Exposure
- Cash & Financing
- Journal
- Trial Balance
- P&L Attribution
- Audit Trail

## 6.4 Desktop-local API direction

The standalone web dashboard has been retired. The remaining supporting surface should stay local and API-first so the desktop workstation, Swagger, and automation can share the same read models without reintroducing a browser product:

- run browser and strategy-state queries through retained workstation APIs
- portfolio summary and cash / ledger inspection through localhost routes
- lightweight diagnostics and audit access for desktop tooling

---

## 7. Migration Phases

## Phase 0 — Documentation and IA alignment

**Goal:** Align repository docs around the new target state before implementation work begins.

**Deliverables**
- this blueprint
- roadmap updates
- feature inventory updates
- production status updates
- WPF documentation updates
- architecture documentation updates

## Phase 1 — Navigation and workspace restructuring

**Goal:** Make new functionality discoverable without requiring core engine rewrites.

**Current status (2026-04-25):** Baseline implemented in WPF. `ShellNavigationCatalog`, workspace shell pages, command/search metadata, shared deep-page hosting, shell context strips, and shell/navigation smoke tests are present. Continue validating this phase through active workflows rather than adding more navigation structure for its own sake.

**Work**
- Register all existing trading/backtesting pages consistently in WPF navigation.
- Add command palette entries for backtest, trading, and portfolio-ledger workflows.
- Consolidate top-level navigation into `Research`, `Trading`, `Data Operations`, `Governance`.
- Add cross-links between backtest, Lean, live viewer, and portfolio import flows.

**Exit criteria**
- Every major trading workflow is reachable from primary navigation and command palette.
- No major capability exists only as an orphan page.

## Phase 2 — Shared Run and Portfolio read models

**Goal:** Unify backtest, paper, and live-facing state around common models.

**Current status (2026-04-25):** Partial. Shared run, portfolio, ledger, reconciliation, and promotion endpoint seams are present, but the roadmap still treats cross-workspace continuity and compatibility governance as Wave 3 / DK2 work.

**Work**
- Introduce shared run DTOs/read models.
- Create a run browser and run-detail view model contract.
- Normalize metrics, fills, cash flows, and portfolio summaries across engines.
- Expose a comparison-friendly results schema.

**Exit criteria**
- A user can compare multiple runs across engines from one surface.

## Phase 3 — Backtest Studio unification

**Goal:** Merge native and Lean backtesting into one cohesive experience.

**Work**
- Replace one-off backtest launcher patterns with a common Backtest Studio shell.
- Support engine selection (`Meridian Native` / `Lean`).
- Add parameter editing, benchmark selection, coverage preflight, and saved scenarios.
- Add compare-runs and open-ledger affordances.

**Exit criteria**
- Backtesting feels like one product capability with multiple engines, not separate tools.

## Phase 4 — Portfolio & Ledger first-class UX

**Goal:** Surface accounting and portfolio state as operator-visible product features.

**Work**
- Build portfolio overview and ledger drill-down views.
- Add trial balance, journal explorer, account summaries, and financing analysis.
- Add “why did equity change?” and “reconcile P&L” views backed by ledger read models.

**Exit criteria**
- Operators can inspect and audit a run without leaving the product or reading raw storage.

## Phase 5 — Paper-trading cockpit and execution hardening

**Goal:** Turn paper trading into a reliable pre-live environment.

**Current status (2026-04-25):** Partial. Paper/execution primitives, cockpit surfaces, and position/order/fill/replay/session paths are present. Promotion rejection outcome severity and audit-history refresh are being hardened, but dependable daily operation is still a Wave 2 / DK1 acceptance problem.

**Work**
- Add positions, orders, fills, exposure, and risk panels.
- Replace scaffold-only market fills with feed-aware simulated pricing.
- Surface risk validator outcomes and strategy controls in real time.
- Add auditability around order lifecycle state transitions.

**Exit criteria**
- Paper trading is usable as a daily validation surface before live promotion.

## Phase 6 — Promotion workflow and live-readiness guardrails

**Goal:** Formalize the controlled path from research to live.

**Work**
- Add promotion workflow: Backtest → Paper → Live
- Capture approvals / checks / preflight validations
- Add environment badges, explicit mode separation, and irreversible-action confirmations
- Keep live routing opt-in and behind explicit safety controls

**Exit criteria**
- Promotion is visible, auditable, and safety-gated.

---

## 8. Repository Documentation Alignment Rules

The following documentation should remain aligned with this blueprint during implementation:

- `README.md`
- `docs/README.md`
- `docs/architecture/ui-redesign.md`
- `docs/status/ROADMAP.md`
- `docs/status/FEATURE_INVENTORY.md`
- `docs/status/production-status.md`
- `docs/status/IMPROVEMENTS.md`
- `src/Meridian.Wpf/README.md`

### Documentation rules during migration

1. Refer to the target UX as **Trading Workstation Migration**.
2. Describe backtesting, paper trading, and future live trading as one lifecycle.
3. Call out **Portfolio + Ledger** as first-class target surfaces.
4. Avoid claiming full UX parity while workflow consolidation is in progress.
5. Distinguish clearly between:
   - current implementation state
   - migration target state
   - post-migration desired state

---

## 9. Risks and Mitigations

| Risk | Why it matters | Mitigation |
|------|----------------|------------|
| UX migration outruns backend contracts | UI becomes another layer of adapters and one-off transforms | Introduce shared run/portfolio/ledger read models first |
| Backtest and Lean remain parallel systems | Users continue to see duplicate concepts | Force common run browser and result schema |
| Paper trading feels misleading | Simulated execution may look more realistic than it is | Keep mode badging explicit; document fill assumptions; add staged realism improvements |
| Ledger remains hidden | Strong engine feature fails to create user value | Make journal, trial balance, and account drill-down mandatory in Phase 4 |
| Documentation drifts again | Roadmap and status docs become contradictory | Update the docs listed in Section 8 in the same PR as each phase transition |

---

## 10. Success Metrics

The migration should be considered successful when the following are true:

### Product / UX
- Users can navigate the platform through four workspaces rather than dozens of loosely-related pages.
- Backtest, paper, and live capabilities share a recognizable run model.
- Portfolio and ledger views are first-class navigation destinations.

### Engineering
- WPF page logic relies on workflow view models and orchestration services rather than page-local business logic.
- Backtest result schemas are comparable across native and Lean engines.
- Trading state can be queried through stable read models.

### Operational
- Paper mode and live mode are visually and operationally distinct.
- Promotion checks are auditable.
- Strategy runs, fills, P&L, and ledger movements can be reconciled from the product UI.

---

## 11. Immediate Next Actions

1. Align documentation and status reporting around this blueprint.
2. Validate the implemented WPF shell baseline against real `Research`, `Trading`, `Data Operations`, and `Governance` workflows.
3. Keep shared `StrategyRun`, `PortfolioSummary`, `LedgerSummary`, reconciliation, and promotion contracts under the compatibility matrix.
4. Tie cockpit acceptance to DK1 provider trust, replay/sample parity, promotion rationale, and session/replay reliability.
5. Prioritize Phase 2, Phase 4, and Phase 5 work that reduces page-local orchestration and strengthens shared contracts rather than broadening shell surface area.
