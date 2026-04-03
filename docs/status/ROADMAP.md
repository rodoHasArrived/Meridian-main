# Meridian - Project Roadmap

<<<<<<< Updated upstream
**Last Updated:** 2026-04-03
**Status:** Active productization — WPF workstation refresh progressing, provider confidence and paper cockpit on critical path
**Repository Snapshot (2026-04-03):** solution projects: 39 | `src/` projects: 28 | test projects: 9 | workflow files: 38
=======
**Last Updated:** 2026-04-01
**Status:** Combined roadmap refresh aligned to the current repository, active workstation direction, and fund-operations target state
**Repository Snapshot (2026-04-01):** solution projects: 36 | `src/` project files: 28 | test projects: 8 | workflow files: 38
>>>>>>> Stashed changes

Meridian is no longer primarily blocked on missing platform primitives. The core platform baseline is now broad and structurally strong across ingestion, storage, backtesting, execution, ledger, workstation contracts, and governance foundations. The main delivery problem has shifted from "can Meridian do this at all?" to "which operator workflows are complete enough to trust, operate, and audit end to end?"

The active roadmap therefore centers on four outcomes:

- finish the reliability evidence that makes the data and execution layers trustworthy
- turn the existing execution and promotion APIs into a real paper-trading cockpit
- make strategy runs, portfolio, ledger, and reconciliation feel like one connected product
- complete the workstation and governance productization work without forking the architecture

Use this document with:

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) - current capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) - normalized non-assembly backlog
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md) - completed improvement history
- [`production-status.md`](production-status.md) - current readiness posture and pre-production gaps
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) - workstation target state
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) - governance target state

---

## Summary

Meridian's core platform pillars remain the same: data collection, backtesting, execution, and portfolio or strategy tracking. What has changed since earlier roadmap versions is the degree of completion underneath them. The repository now includes:

- a large provider and storage baseline with WAL, JSONL, Parquet, backfill scheduling, gap analysis, and data quality monitoring
- unified workstation-oriented contracts and bootstrap endpoints in `src/Meridian.Contracts/Workstation/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- a React workstation shell with Research, Trading, Data Operations, and Governance routes in `src/Meridian.Ui/dashboard/src/app.tsx`
- shared run, portfolio, ledger, and reconciliation read services in `src/Meridian.Strategies/Services/`
- execution, promotion, and strategy lifecycle APIs already wired in `ExecutionEndpoints.cs`, `PromotionEndpoints.cs`, and `StrategyLifecycleEndpoints.cs`
- Security Master, ledger, reconciliation, direct-lending, and brokerage gateway foundations already in code

That means the roadmap should now prioritize workflow completion, operator trust, governance productization, and product integration over more platform-sprawl.

---

## Current State

### Complete

These are conservative "in code and materially usable" claims as of 2026-04-01:

- Core ingestion pipeline: bounded channels, backpressure handling, WAL durability, composite sinks, and graceful shutdown
- Storage foundation: JSONL and Parquet sinks, tiered storage, packaging, export, lineage, catalog, quota enforcement, and lifecycle policies
- Broad provider baseline: streaming providers, historical providers, symbol search, fallback chains, and provider registration through DI
- Data quality foundation: completeness, gap analysis, sequence checks, anomaly detection, freshness SLA monitoring, degradation scoring, and quality reporting
- Backtesting foundation: native replay engine, Lean integration, strategy SDK, run storage, and export
- Execution foundation: paper trading gateway, risk rules, order abstractions, brokerage gateway framework, and REST endpoints for account, orders, sessions, health, and promotion
- Workstation contracts and read models: shared strategy-run, portfolio, ledger, reconciliation, and Security Master DTO surfaces
- React workstation shell: routed Research, Trading, Data Operations, and Governance screens with shared bootstrap loading
- WPF workstation baseline: broad page inventory plus Strategy Runs, run detail, portfolio, ledger, Security Master, and QuantScript surfaces
- Governance foundations: ledger kernel, Security Master services, reconciliation DTOs and endpoints, direct-lending vertical slice, and export/reporting primitives
- Improvement portfolio A-G and J: the active improvement tracker marks the historical platform-improvement set as complete
- WPF workstation shell modernization: native Fluent theme via `ThemeMode="System"` (PR #524), SVG icon set replacing emoji glyphs (PR #512), LiveCharts2 candlestick charting on the Charting page (PR #522), and zero-API-key startup via Synthetic provider default (PR #513)
- Route and health endpoint reliability: duplicate DFA route definitions and duplicate health endpoint registrations resolved (PRs #521, #519)
- Workflow guide and live screenshots: `docs/WORKFLOW_GUIDE.md` with live UI screenshots checked in (PR #511); CI screenshot-refresh workflow added (PR #515)

### Partial

These areas exist in code but are not yet complete enough to treat as finished operator workflows:

- Provider confidence remains uneven. Polygon replay depth, IB runtime validation, NYSE lifecycle hardening, and StockSharp validated adapter breadth still need stronger evidence.
- Backfill reliability is broadly implemented but still needs longer-run checkpoint validation and clearer operator trust signals across providers and date ranges.
- The React workstation shell is real, but several screens still behave like summary-oriented shells over prefetched data rather than fully closed operator workflows.
- The paper-trading cockpit exists as APIs plus a React trading screen, but end-to-end operator depth, live vendor validation, and session replay still need work.
- Shared run, portfolio, ledger, fills, attribution, and reconciliation read paths exist, but cross-mode run history and comparison depth are not yet a finished product.
- Security Master has real services and workstation models, but full downstream integration into governance, portfolio, ledger, and reconciliation workflows is still incomplete.
- WPF workstation shell modernization has landed (Fluent theme, SVG icons, candlestick charting), but the deeper migration from page-dense navigation to workflow-first orchestration — particularly MVVM extraction on high-traffic pages (Live Data, Provider, Backfill, Data Quality) — is still in progress.

### Planned

These are active productization tracks rather than greenfield invention:

- complete the web paper-trading cockpit and promotion UX on top of the existing execution and promotion endpoints
- deepen run browser, comparison, portfolio drill-in, attribution, and ledger views around the shared run model
- unify native and Lean backtesting into one Backtest Studio workflow
- validate one or more brokerage adapters against live vendor surfaces with audit trail and human approval controls
- finish Security Master and governance productization around account or entity structure, reconciliation, multi-ledger views, cash-flow analysis, and report packs
- continue the workstation migration so Research, Trading, Data Operations, and Governance feel like coherent workspaces rather than grouped pages

### Optional

These remain valuable but are not on the critical path to Meridian's core operator-ready product:

- QuantScript deeper workflow integration and a larger script/sample library
- L3 inference and queue-aware execution simulation
- multi-instance coordination as a supported scale-out topology
- Phase 16 assembly-level performance optimization
- broader advanced research tooling after the core workstation workflows are operator-ready
- Phase 1.5 preferred and convertible equity domain extension: `EquityClassification` discriminated union, `PreferredTerms`, `ConvertibleTerms`, and `LiquidationPreference` types in `src/Meridian.FSharp/Domain/SecurityMaster.fs` (issue tracked in `issues/phase_1_5_1_add_equityclassification_discriminator_and_preferredterms_domain_model.md`)

---

## What Is Complete

### Platform baseline

- Meridian's ingestion and storage stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and strong observability and operational-readiness foundations.
- The historical improvement backlog is effectively closed for the current baseline, which is a meaningful shift from earlier roadmap snapshots.

### Execution and workflow foundations

- The execution side now has more than infrastructure: account, positions, portfolio, orders, sessions, promotion, and lifecycle endpoints are already present.
- Brokerage gateway abstractions and provider-specific adapters are in place, even if live validation remains incomplete.
- Shared strategy-run contracts now give the project a stable seam for unifying backtest, paper, live-adjacent, portfolio, and governance surfaces.

### Workstation and governance foundations

- The four-workspace model is present in both planning and implementation.
- The React dashboard already boots those workspaces from explicit workstation endpoints.
- WPF already contains a broad set of pages and view models that map to the workstation direction.
- Governance is no longer hypothetical: Security Master, reconciliation DTOs, ledger read paths, and direct lending are real foundations in the repo.

---

## What Remains

### Critical path

- finish provider-confidence validation so data trust is evidence-based, not assumed
- complete the paper-trading cockpit in the web workstation with positions, orders, fills, risk, promotion, and replay depth that operators can actually use
- extend the shared run model into a complete run-history and comparison workflow spanning backtest, paper, and live-adjacent modes
- deepen portfolio, attribution, fills, ledger, and reconciliation drill-ins so they support iteration and audit, not just status summaries
- validate at least one brokerage path against a real vendor surface with audit trail and human-approval controls

### Next major productization layer

- finish Security Master as a shared operator capability rather than a backend-only layer
- connect governance surfaces across account or entity structure, reconciliation, cash-flow, multi-ledger, and report-pack workflows
- keep the workstation migration centered on orchestration services and shared read models instead of multiplying page-local logic

### Later but still meaningful

- deepen QuantScript integration into research workflows
- turn multi-instance coordination into an explicitly supported topology
- bring L3 inference and queue-aware execution simulation online once core strategy workflows are stable

---

## Opportunities

### 1. Workflow completion

**Gap:** Meridian now has many of the right contracts, services, and endpoints, but several operator journeys still end at a summary page or a partial handoff.

**Value:** Closing those journeys produces the biggest visible product gain because the platform already has substantial depth underneath.

**Unlocks:** Credible workstation demos, cleaner handoff from research to trading, and a much clearer definition of "operator-ready."

**Placement:** Critical path.

### 2. Operator UX

**Gap:** The workstation shells exist, especially in the React dashboard, but some areas still read as bootstrap summaries more than fully operational tools.

**Value:** Better drill-ins, actions, and workflow continuity will make Meridian feel like one system instead of multiple strong subsystems.

**Unlocks:** Faster adoption of the four-workspace model and reduced need to context-switch between pages and APIs.

**Placement:** Critical path.

### 3. Provider readiness

**Gap:** Provider breadth is impressive, but trust is still uneven where replay evidence or real-vendor validation is thin.

**Value:** This directly affects confidence in backtests, paper sessions, and eventual live-readiness claims.

**Unlocks:** Safer execution productization and clearer release gates.

**Placement:** Critical path.

### 4. Reliability and observability

**Gap:** Most of the reliability mechanisms exist, but checkpoint confidence, provider evidence, and execution audit trail still need stronger closure.

**Value:** Operator trust depends on proving that the system behaves correctly at restart, reconnect, replay, and promotion boundaries.

**Unlocks:** Production-readiness claims that are evidence-backed rather than architectural.

**Placement:** Critical path.

### 5. Governance productization

**Gap:** Governance has strong ingredients in code, but the finished operator experience across Security Master, account or entity structure, reconciliation, multi-ledger, cash-flow, and reporting is not yet complete.

**Value:** This is a flagship differentiator because it extends Meridian beyond strategy tooling into fund-operations workflow.

**Unlocks:** A true front-, middle-, and back-office narrative built on shared platform seams.

**Placement:** Later wave, but strategically high value.

### 6. Architecture simplification

**Gap:** The workstation migration risks re-creating page-level orchestration if workflow services and shared read models are not kept as the integration seam.

**Value:** A cleaner seam now will make future workstation and governance work cheaper and safer.

**Unlocks:** Faster iteration across web and WPF without duplicated behavior.

**Placement:** Continuous supporting track.

### 7. Flagship product capabilities

**Gap:** Meridian already has the ingredients for a distinctive self-hosted fund-management product, but the product story is still fragmented between strong workstation foundations and still-emerging governance workflows.

**Value:** Converging strategy runs, portfolio, ledger, Security Master, direct lending, and governed reporting into one system is what makes Meridian more than a capable trading stack.

**Unlocks:** A differentiated end-state that connects research, execution, portfolio oversight, and fund-operations without relying on disconnected external tools.

**Placement:** Begins in later waves, but should shape decisions in every active wave.

---

## Target End Product

When this roadmap is finished, Meridian is a workflow-centric trading workstation and fund-operations platform for a self-hosted operator.

The operator can:

- acquire and validate market data from multiple providers with visible confidence and quality signals
- research strategies in one Backtest Studio that spans native and Lean-backed runs
- promote a run into paper trading through an explicit, auditable workflow
- operate paper and later live-adjacent trading from a real cockpit with orders, fills, positions, risk controls, and session history
- inspect run history, portfolio state, attribution, fills, and ledger outcomes through one shared strategy-run model
- manage Security Master issues, account or entity structure, reconciliation breaks, cash-flow questions, direct-lending workflows, and reporting outputs inside the Governance workspace instead of using disconnected tools

The major product surfaces are:

- **Research** for datasets, experiments, backtests, comparisons, and export
- **Trading** for orders, fills, positions, promotion, and risk-managed operation
- **Data Operations** for providers, symbols, backfills, storage, and operational export
- **Governance** for portfolio, ledger, reconciliation, Security Master, cash-flow, and governed reporting

First-class capabilities in the finished product are:

- data trust and provider validation
- backtest to paper workflow continuity
- shared run, portfolio, and ledger visibility
- governance and auditability built on the same platform model
- Security Master and fund-operations tooling integrated into the same operator workflow

Optional capabilities remain optional:

- L3 inference and queue-aware simulation
- multi-instance scale-out
- deeper QuantScript libraries and advanced research extensions
- assembly-level optimization beyond what the core product requires

---

## Recommended Next Waves

### Wave 1: Provider confidence and trust closure

**Why now:** This is still the main dependency for every downstream claim Meridian wants to make.

**Focus:**

- expand Polygon replay and live-feed evidence
- keep IB runtime validation aligned with current vendor surfaces
- harden NYSE lifecycle and cancellation behavior
- expand validated StockSharp adapter coverage
- validate backfill checkpoints and gap handling across longer date ranges

**Exit signal:** Every major provider has documented replay or runtime evidence and backfill reliability is validated across representative ranges.

### Wave 2: Web paper-trading cockpit completion

**Why now:** Meridian already has the execution, session, and promotion APIs. Product value now depends on finishing the operator cockpit.

**Focus:**

- deepen the React trading workspace from summary shell to real operator cockpit
- complete positions, orders, fills, P&L, risk, and promotion actions
- add paper-session replay from persisted history
- verify brokerage adapter behavior through the cockpit flows

**Exit signal:** A strategy can move from backtest into a visible, auditable paper-trading workflow in the web workstation.

### Wave 3: Shared run model productization

**Why now:** The contracts exist, but the product experience around them is not yet fully realized.

**Focus:**

- complete run browser and run-detail depth across backtest, paper, and live-adjacent modes
- strengthen comparison, attribution, fills, and equity-curve workflows
- make portfolio and ledger drill-ins part of the same run-centered experience

**Exit signal:** Strategy runs are Meridian's primary product object across research, trading, and governance.

### Wave 4: Backtest Studio unification

**Why now:** Research remains split until native and Lean-backed workflows feel like one product.

**Focus:**

- unify native and Lean results under one result model
- improve comparison and run-diff tooling
- broaden fill-model realism
- improve performance for larger windows where it materially changes operator experience

**Exit signal:** Backtesting feels like one coherent workflow regardless of engine.

### Wave 5: Live integration readiness

**Why now:** Live-adjacent credibility should follow, not precede, a finished paper workflow and validated provider trust.

**Focus:**

- validate at least one brokerage path against a real vendor surface
- add execution audit trail and human approval controls
- define safe `Paper -> Live` promotion gates
- formalize operator controls such as manual overrides, circuit breakers, and intervention flows

**Exit signal:** Meridian can support a controlled live-readiness story without overclaiming broad live-trading completion.

### Wave 6: Governance and Security Master productization

**Why now:** This is Meridian's strongest strategic expansion once the core run and execution flows are credible.

**Focus:**

- finish Security Master workflow integration across workstation, portfolio, ledger, and reconciliation reads
- add account or entity structure, governance navigation, and shared operator context instead of isolated fund-ops pages
- deepen reconciliation queues and resolution workflows
- add multi-ledger, trial-balance, and cash-flow views
- generate governed report packs from shared export and audit seams

**Exit signal:** Governance is a genuine operator workflow, not just a set of strong backend foundations.

### Optional wave: Advanced research and scale

**Focus:**

- QuantScript deeper integration
- L3 inference and queue-aware execution simulation
- multi-instance coordination
- Phase 16 performance work

**Exit signal:** These deepen Meridian's ceiling after the core workstation product is complete.

---

## Risks and Dependencies

- **Provider trust is still the first dependency.** Without replay and runtime evidence, downstream product polish risks overstating readiness.
- **Cockpit completion should precede live-readiness claims.** Meridian now has the APIs and adapters to make this tempting, but operator workflow depth still matters more than breadth.
- **The shared run model must remain the center of gravity.** If backtest, paper, portfolio, ledger, and governance flows drift apart again, the workstation migration loses its product logic.
- **Security Master must integrate through shared read models.** It should enrich portfolio, ledger, and governance flows rather than becoming its own parallel subsystem.
- **Workstation delivery should avoid page-level duplication.** The right move is more orchestration and shared DTOs, not more disconnected UI logic.
- **Governance should follow credible core workflows.** Its strategic value is high, but it lands best once strategy run, portfolio, and paper-trading seams are trustworthy.

---

## Release Gates

Meridian can reasonably claim core operator-readiness when all of the following are true:

1. Major providers have replay or runtime validation evidence with documented confidence gaps closed or explicitly bounded.
2. The web workstation exposes a complete paper-trading cockpit on top of the existing execution and promotion APIs.
3. `Backtest -> Paper` is explicit, auditable, and operator-visible.
4. Run history, portfolio, fills, attribution, and ledger views are connected through one shared run model across backtest and paper flows.
5. Backfill checkpoints and data-gap handling are validated across representative providers and date ranges.

Until then, Meridian is best described as feature-rich, structurally strong, and actively being productized into its intended workstation end state.

---

## Reference Documents

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md)
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md)
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md)
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
