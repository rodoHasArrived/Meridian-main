# Trading Workstation Migration Blueprint

**Owner:** Core Team
**Audience:** Product, Architecture, Desktop, API, and Platform contributors
**Last Updated:** 2026-05-20

## TODO Checklist (Concrete Implementation Items)
- [ ] Define scope boundaries for **trading workstation migration blueprint** and document explicit in-scope vs out-of-scope items.
- [ ] Break delivery into PR-sized milestones with owner, dependency, and evidence artifact for each milestone.
- [ ] Implement the first milestone in code/config/scripts and link the exact validating test or command output.
- [ ] Add/update operator runbook steps and rollback procedure for the trading workstation migration blueprint workflow.
- [ ] Record completion evidence in `docs/status/` (or linked packet) and mark corresponding checklist items done.

**Status:** Active blueprint — browser and WPF are both active operator UI lanes, with business behavior owned by shared contracts, local/web API endpoints, and shared read models before either client composes it. The WPF shell/navigation baseline remains implemented support evidence; signed DK1 trust-gate state and risk/control audit explainability now project into the trading readiness lane and retained WPF Trading desk briefing hero with stale replay count detail plus warning/critical shared-work-item blockers. The web Research run library now provides the first browser support slice for retained-run review, two-run compare/diff readiness, promotion-history loading, command-error alerts, and refreshed built workstation assets. Workflow validation and cockpit/shared-model/governance hardening remain in progress.

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

However, those capabilities are still exposed through multiple page- and service-centric flows. The active operator UI lanes are now the browser workstation and WPF workstation, with the WPF shell carrying a seven-workspace baseline, metadata-driven navigation, command/search metadata, shared deep-page hosting, context strips, Trading, Research, and Data desk briefing heroes, Trading Hours session briefing, OrderBook order-flow posture, Provider Health posture briefing, System Health triage, Notification Center filter recovery, Activity Log triage/export/clear support, Watchlist posture with pinned-first card ordering, Messaging Hub delivery posture with refresh recency, StrategyRuns filter recovery, BatchBacktest results empty guidance, QuantScript run-history handoffs, Security Master runtime/search recovery, Fund Accounts account-queue/provider-routing/shared-data and balance-evidence briefing, route-aware account-scoped shell queue-button consumption of the shared operator inbox with shell-context attention cues, smoke coverage, workflow page-state automation markers, corrected isolated restore/build behavior, and local single-instance mutex plus launch-argument forwarding coverage, but the product still needs to prove that both active client workflows consume the same shared model instead of drifting into parallel products.

The newest shell support slice adds Welcome readiness progress for provider connection, symbol inventory, and storage-path posture; Storage preview scope/guidance for archive-path decisions; OrderBook order-flow posture for depth/tape/spread monitoring; compact command-bar presentation for shared deep-page hosts while retaining related-workflow and trust-state context; actionable shell-context attention detail; provider-degradation workflow summaries that hand off to `ProviderHealth`; brokerage-sync queue routing into `AccountPortfolio`; and Trading desk hero attention states for warning or critical shared readiness work items. These are useful operator-orientation improvements, but they remain evidence for validating the workstation migration rather than a separate completion claim.

### Current pain points

1. **Backtesting is still split across multiple experiences**
   - Native WPF backtest page
   - Lean integration page
   - no unified run browser or comparison workflow

2. **Paper trading is still infrastructure-first, operator-second**
   - OMS and paper gateway exist
   - trading cockpit, positions, blotter, and risk surfaces exist, but daily-use acceptance still depends on preserving DK1 trust evidence, promotion rationale, replay/session reliability, and operator scenario sign-off

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
- Consolidate visible product navigation into **workflow workspaces** while retaining WPF
  compatibility routes only where they support shared contracts or desktop support.
- Unify native and Lean backtesting under a single operator-facing model.
- Evolve paper trading into a realistic pre-live operating environment.

### Non-goals

- Rewriting core ingestion, storage, or provider abstractions.
- Replacing the WPF shell in this migration.
- Removing Lean integration.
- Introducing real-money broker routing by default.

---

## 4. Target Product Information Architecture

The target UX is organized around seven visible browser-workstation workspaces:
`Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
Retained WPF compatibility routes may still use legacy labels such as `Research`,
`Data Operations`, and `Governance`, but those names are aliases or planning shorthand, not the
default product navigation.

## 4.1 Data

**Purpose:** Establish trusted provider, symbol, storage, replay, backfill, and export evidence.

**Consolidates / fronts:**

- provider setup and health
- live quotes, order book, watchlists, and price alerts
- backfill, storage, symbol, package, and data-quality workflows
- Security Master reference-data readiness where it is part of data trust

## 4.2 Strategy

**Purpose:** Turn trusted data into reviewed runs, comparisons, promotion packets, QuantScript
handoffs, visual strategy design, and research evidence.

**Consolidates / fronts:**

- Backtest and Lean research surfaces
- run library, run comparison, promotion history, and review-packet workflows
- QuantScript, Quant Lab, notebook, and strategy-designer workflows
- research exports when used as evidence for a run or promotion

**Primary tasks:**

- choose dataset and strategy inputs
- run backtests
- compare multiple runs
- inspect fills and attribution
- review promotion readiness
- export research outputs

## 4.3 Trading

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

## 4.4 Portfolio

**Purpose:** Inspect account posture, positions, exposure, attribution, household rollups, brokerage
sync evidence, and balance evidence created by research, paper, and read-only account-sync flows.

**Primary tasks:**

- review positions and exposure
- inspect brokerage-sync freshness and divergence
- drill from run or symbol context into portfolio impact
- hand off to Trading readiness, Accounting, or Reporting evidence when needed

## 4.5 Accounting

**Purpose:** Review ledger, cash-flow, trial-balance, reconciliation, Security Master, and sign-off
casework on the same shared evidence model.

**Consolidates / fronts:**

- run ledger, cash-flow, trial-balance, and reconciliation views
- Security Master coverage, identity, lot, override, and conflict review
- fund/account/entity posture where the workflow is accounting-led

## 4.6 Reporting

**Purpose:** Convert governed evidence into retained report packs, exports, approvals, and
restatement-ready artifacts.

**Primary tasks:**

- preview report-pack readiness
- inspect evidence packets and graph lineage
- generate, approve, export, and retain governed outputs
- drill report lines back to source run, portfolio, ledger, reconciliation, and approval evidence

## 4.7 Settings

**Purpose:** Keep credentials, storage roots, backend capability coverage, environment posture, and
operator configuration reproducible.

**Primary tasks:**

- configure providers and credential posture
- repair setup blockers from readiness workflows
- review API capability coverage and storage/evidence paths
- keep paper-first and live-gated workflow settings explicit

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

## 6.1 Strategy workspace

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

## 6.3 Portfolio, Accounting, and Reporting workspaces

**Primary surfaces**

- Portfolio: overview, positions, exposure, attribution, brokerage-sync posture, and balance evidence
- Accounting: cash and financing, journal, trial balance, reconciliation, Security Master coverage, and sign-off casework
- Reporting: report-pack readiness, evidence workbench, artifact generation, approval, retention, and provenance drill-ins

## 6.4 Web workstation and local API direction

The standalone-browser direction remains active through the React/Vite dashboard, and the WPF workstation is also active for Windows desktop workflows. The local API should remain the shared contract source so the web workstation, WPF shell, Swagger, and automation consume the same read models instead of drifting into separate products:

- run browser, strategy-state, compare, diff, and promotion-history queries through shared workstation APIs
- portfolio summary and cash / ledger inspection through localhost routes
- lightweight diagnostics and audit access for web, desktop-support, and automation tooling

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

**Current status (2026-05-12):** Retained WPF compatibility baseline implemented. `ShellNavigationCatalog`, workspace shell pages, command/search metadata, shared deep-page hosting, shell context strips, Trading, Research, and Data desk briefing heroes, Trading Hours session briefing, OrderBook order-flow posture, Provider Health posture briefing, System Health triage, Notification Center filter recovery, Activity Log triage/export/clear state, Watchlist posture with pinned-first card ordering, Messaging Hub delivery posture with refresh recency, StrategyRuns filter-aware recovery/run-scope presentation, BatchBacktest results empty guidance, QuantScript local Run History with Research handoffs for mirrored runs, Security Master runtime/search recovery, Fund Accounts operator and balance-evidence briefing, shell operator queue button state with shell-context attention cues, shell/navigation smoke tests, `ShellAutomationState` page-state confirmation, deterministic isolated workflow restore/build behavior, local single-instance mutex plus launch-argument forwarding coverage, and the feature-owned `src/Meridian.Wpf/Features/Data/` shell module are present. Continue validating this phase through active browser workflows and retained compatibility checks rather than adding more navigation structure for its own sake.

Welcome readiness progress, Storage preview scope/guidance, OrderBook order-flow posture, compact shared deep-page command chrome, related-workflow/trust-state context around hosted pages, actionable shell-context attention detail, `ProviderHealth` handoffs for provider degradation, brokerage-sync queue routing into `AccountPortfolio`, and Trading hero attention states for warning or critical shared work items are also present as shell-support evidence.

**Work**

- Register all existing trading/backtesting pages consistently in WPF navigation.
- Add command palette entries for backtest, trading, and portfolio-ledger workflows.
- Keep top-level navigation aligned to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`, with `Research`, `Data Operations`, and `Governance` retained only as compatibility aliases or planning shorthand.
- Add cross-links between backtest, Lean, live viewer, and portfolio import flows.

**Exit criteria**

- Every major trading workflow is reachable from primary navigation and command palette.
- No major capability exists only as an orphan page.

## Phase 2 — Shared Run and Portfolio read models

**Goal:** Unify backtest, paper, and live-facing state around common models.

**Current status (2026-04-27):** Partial. Shared run, portfolio, ledger, reconciliation, and promotion endpoint seams are present, but the roadmap still treats cross-workspace continuity and compatibility governance as Wave 3 / DK2 work.

RunCashFlow now has a WPF evidence-state pass for selected-run, missing-run, no-event, and loaded retained cash-flow summaries. Treat it as a shared-run continuity improvement alongside run detail, portfolio, ledger, StrategyRuns, BatchBacktest, and QuantScript handoffs; broader governance cash-flow projection still belongs to the fund-ops wave.

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

## Phase 4 — Portfolio, Accounting, and Reporting first-class UX

**Goal:** Surface portfolio, ledger, accounting, reconciliation, and governed-output state as
operator-visible product features.

**Work**

- Build portfolio overview and ledger drill-down views.
- Add trial balance, journal explorer, account summaries, and financing analysis.
- Add “why did equity change?” and “reconcile P&L” views backed by ledger read models.

**Exit criteria**

- Operators can inspect and audit a run without leaving the product or reading raw storage.

## Phase 5 — Paper-trading cockpit and execution hardening

**Goal:** Turn paper trading into a reliable pre-live environment.

**Current status (2026-04-27):** Partial. Paper/execution primitives, cockpit surfaces, signed DK1 trust-gate readiness projection, position/order/fill/replay/session paths, Trading Hours session briefing, OrderBook order-flow posture, account-scoped route-aware operator queue handling with shell-context attention cues and Account Portfolio brokerage-sync routing, and promotion checklist/audit evidence are present. Dependable daily operation is still a Wave 2 / DK1 acceptance problem.

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
- `archive/docs/assessments/ui-redesign.md` (historical UI redesign context only)
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
| --- | --- | --- |
| UX migration outruns backend contracts | UI becomes another layer of adapters and one-off transforms | Introduce shared run/portfolio/ledger read models first |
| Backtest and Lean remain parallel systems | Users continue to see duplicate concepts | Force common run browser and result schema |
| Paper trading feels misleading | Simulated execution may look more realistic than it is | Keep mode badging explicit; document fill assumptions; add staged realism improvements |
| Ledger remains hidden | Strong engine feature fails to create user value | Make journal, trial balance, and account drill-down mandatory in Phase 4 |
| Documentation drifts again | Roadmap and status docs become contradictory | Update the docs listed in Section 8 in the same PR as each phase transition |

---

## 10. Success Metrics

The migration should be considered successful when the following are true:

### Product / UX

- Users can navigate the product through the seven-workspace model in browser and WPF surfaces instead of dozens of loosely-related pages.
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
2. Validate the browser workstation and WPF shell against real `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` workflows.
3. Keep shared `StrategyRun`, `PortfolioSummary`, `LedgerSummary`, reconciliation, and promotion contracts under the compatibility matrix.
4. Tie cockpit acceptance to DK1 provider trust, replay/sample parity, promotion rationale, and session/replay reliability.
5. Keep the Trading desk briefing hero consuming shared active-run, workflow-summary, and operator-readiness state, including risk/control audit explainability, rather than becoming a shell-local readiness model.
6. Keep the Data desk briefing hero and Provider Health posture briefing consuming shared provider, backfill, storage, session, and export state through the feature-owned Data shell module rather than becoming shell-local operational-readiness models.
7. Keep System Health triage as diagnostics support evidence for provider, storage, and retained event posture, including pending-scan versus confirmed-empty guidance, not a substitute for readiness gates or durable incident queues.
8. Keep Notification Center filter recovery as governance/operator-triage support evidence for retained history recovery, not a substitute for durable work-item queues.
9. Keep Activity Log triage and export/clear header actions as supporting operational review evidence for errors, warnings, latest activity, active filters, and support traces, not a substitute for readiness gates.
10. Keep Watchlist posture and pinned-first display as symbol-set staging guidance for saved lists, pinned lists, search scope, and symbol coverage, not as a separate readiness gate.
11. Keep OrderBook order-flow posture as depth/tape/spread monitoring support evidence, not as a separate execution-readiness model.
12. Keep StrategyRuns filter recovery as shared-run support evidence for visible-versus-recorded scope and retained-row recovery, not as a separate run-store or readiness model.
13. Keep run review-packet work items in the operator inbox bounded to actionable warning/critical latest-run blockers, not a broad run-history or acceptance replacement.
14. Keep Storage archive posture as Data Operations support evidence for growth, capacity horizon, scan failures, and archive handoffs, not as a separate storage readiness gate.
15. Keep the GitHub WPF screenshot refresh workflow as validation evidence capture for catalog/manual shell states, with diagnostics and one final screenshot commit, not as a substitute for workflow acceptance.
16. Keep QuantScript Run History as research support evidence for local execution records and mirrored-run handoffs into shared Research surfaces, not as closure of broader Backtest Studio unification.
17. Keep Fund Accounts operator and balance-evidence briefing as governance support evidence for shared account, provider-routing, retained balance-history, and shared-data-access posture, not as closure of external-account or durable casework readiness.
18. Prioritize Phase 2, Phase 4, and Phase 5 work that reduces page-local orchestration and strengthens shared contracts rather than broadening shell surface area.
