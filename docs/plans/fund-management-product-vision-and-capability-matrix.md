# Meridian Fund Management Product Vision and Capability Matrix

**Owner:** Core Team
**Audience:** Product, architecture, engineering, operations, and stakeholders
**Last Updated:** 2026-04-27
**Status:** Active planning document; subordinate to the canonical wave model in [`../status/ROADMAP.md`](../status/ROADMAP.md)

> Current-state refresh (2026-04-27): keep this matrix as a broad product-vision view, not a
> parallel delivery plan. Canonical wave status and dates live in
> [`../status/PROGRAM_STATE.md`](../status/PROGRAM_STATE.md). The WPF shell baseline, delivered
> Security Master seam, fund-structure services, cash-flow projections, first governed report-pack
> path, file-backed reconciliation break queue, route-aware operator-inbox handling, Security
> Master search recovery, and Fund Accounts operator briefing have moved several rows from
> "planned" to "in progress" or "implemented baseline"; operator-readiness still depends on the
> active Wave 2-4 gates.

## 1. Product Vision

Meridian's target end state is a comprehensive self-hosted fund management platform that supports the full investment and operations lifecycle across front office, middle office, and back office workflows.

The product should let a fund operator move through one connected system for:

- account and entity management
- Security Master and reference data management
- strategy research and backtesting
- portfolio construction and implementation
- order, execution, and trade management
- portfolio accounting and multi-ledger operations
- reconciliation and break management
- cash-flow, NAV, and trial-balance analysis
- compliance, governance, and audit workflows
- investor, board, and stakeholder reporting

Meridian should feel like one coherent operating platform rather than a collection of unrelated tools, pages, and utilities.

## 2. Product Principles

- **One lifecycle, one product:** research, implementation, trading, accounting, and reporting should share common objects and workflows.
- **Operator-first:** workflows should be optimized for PMs, traders, operations, finance, compliance, and leadership users.
- **Local-first and auditable:** storage, replay, reconciliation, and reporting should preserve traceability and operator control.
- **Shared canonical objects:** accounts, entities, strategies, runs, positions, instruments, cash, journals, and reports should be modeled consistently.
- **Progressive productization:** existing repo foundations should be elevated into product workflows instead of replaced by parallel subsystems.

## 3. Product Operating Model

The target product is organized into four top-level product surfaces:

| Surface | Purpose |
| --------- | --------- |
| Research | Strategy ideation, data validation, backtesting, analytics, and experiment comparison |
| Trading | Implementation, order workflow, execution monitoring, positions, and trade-management operations |
| Data Operations | Providers, symbols, backfills, storage, replay, exports, and data quality |
| Governance | Security Master, accounts, entities, ledgers, reconciliation, cash-flow, reporting, and audit workflows |

These surfaces are user-facing shells, not isolated subsystems. They should share common identifiers, shared read models, and consistent drill-through behavior.

## 4. Capability Domains

| Domain | Scope |
| -------- | ------- |
| Front office | research, portfolio construction, implementation, order workflow, execution monitoring |
| Middle office | positions, breaks, trade support, Security Master, lifecycle controls, cash oversight |
| Back office | accounting, multi-ledger tracking, trial balance, reconciliation, reporting, audit support |
| Cross-cutting platform | providers, storage, replay, diagnostics, observability, exports, permissions, packaging |

## 5. Phased Capability Matrix

Status legend:

- **Implemented baseline**: meaningful code foundations already exist in the repo
- **In progress**: partially productized and active delivery work
- **Planned**: explicitly in scope but not yet delivered as a first-class product workflow
- **Future**: acknowledged target area beyond the current blueprint slice

| Capability | Domain | Current State | Target Phase | Notes |
| ------------ | -------- | --------------- | -------------- | ------- |
| Market-data ingestion and storage | Cross-cutting platform | Implemented baseline | Phase 0 | Existing platform foundation |
| Replay, export, diagnostics, observability | Cross-cutting platform | Implemented baseline | Phase 0 | Needed to support all later workflows |
| Workspace shell (`Research`, `Trading`, `Data Operations`, `Governance`) | Cross-cutting platform | Implemented baseline | Phase 1 | WPF shell/navigation baseline, context strips, command/search metadata, Trading/Research/Data Operations desk briefing heroes, Provider Health posture, System Health triage with pending-scan versus confirmed-empty guidance, Notification Center filter recovery, Activity Log triage, Watchlist posture, StrategyRuns filter recovery, BatchBacktest results empty guidance, QuantScript local run-history handoffs, Security Master search recovery, and Fund Accounts operator briefing are present; workflow acceptance remains active Wave 2-4 work |
| Shared run model across backtest, paper, and live history | Front office | In progress | Phase 1 | First WPF run browser/detail/portfolio/ledger flow exists; StrategyRuns now shows visible-versus-recorded run scope and recovers filters that hide retained runs; BatchBacktest now distinguishes idle, validation-blocked, running, failed, cancelled, and populated sweep-result states |
| Strategy research and backtesting | Front office | Implemented baseline | Phase 1 | Needs stronger shared-run UX and comparison flows |
| Portfolio construction and implementation | Front office | In progress | Phase 2 | Needs stronger operator workflow, approvals, and cockpit UX |
| Order, execution, and trade management | Front office / Middle office | In progress | Phase 2 | Paper-session, replay, risk/control audit explainability, promotion, signed DK1 trust-gate, trading-readiness, and initial operator-inbox contracts are in code; route-aware account-scoped WPF queue consumption is present, while dependable daily-use acceptance remains Wave 2 work |
| Account and entity management | Middle office / Back office | In progress | Phase 2 | Fund-structure graph, assignment, account, entity, sleeve, vehicle, and environment-design seams exist; Fund Accounts now projects account-queue, provider-routing, shared-data, and reconciliation-readiness states; richer operator review and durable casework flows remain open |
| Security Master productization | Middle office / Cross-cutting platform | Implemented baseline | Phase 2 | Security Master is the delivered authoritative instrument seam across WPF, Research, Trading, Portfolio, Ledger, Reconciliation, and Governance; the WPF browser now includes runtime-unavailable search recovery and bound `Clear Search` reset |
| Position and portfolio oversight | Middle office | In progress | Phase 2 | Shared read models exist; broader operator tooling pending |
| Multi-ledger accounting | Back office | In progress | Phase 3 | Ledger-grouping rules and fund-structure assignment seams exist; consolidation and operator acceptance remain open |
| Trial balance and account-summary analysis | Back office | In progress | Phase 3 | Fund Operations workspace and report-pack services expose trial-balance slices; broader account-summary UX remains open |
| Cash-flow modeling and liquidity views | Middle office / Back office | In progress | Phase 3 | Cash-flow projection and governance cash-flow query seams exist; richer liquidity operator workflows remain open |
| Reconciliation engine and break queues | Middle office / Back office | In progress | Phase 4 | Run-scoped reconciliation and a file-backed break queue exist with seeded exception-route, tolerance, and sign-off metadata; position, cash, custodian, external-statement matching, and operator-approved calibration remain |
| NAV, attribution, and governance exception workflows | Middle office / Back office | In progress | Phase 4 | Shared portfolio, ledger, reconciliation, and governance exception seams exist; generalized durable casework remains open |
| Investor, board, compliance, and fund-ops reporting | Back office | In progress | Phase 5 | Governed report-pack schema/version checks, manifest/provenance artifacts, history, and retrieval are present; publication controls and richer templates remain open |
| Compliance and policy monitoring | Middle office / Back office | Future | Phase 5 | Rule evaluation, mandate monitoring, attestations |
| Full live operating lifecycle with approvals and controls | Front office / Middle office | Future | Phase 6 | Promotion, approvals, overrides, and live-operating guardrails |

## 6. Delivery Phases

### Phase 0: Platform Foundation

Objective: preserve and harden the existing ingestion, storage, replay, export, and diagnostics baseline.

Primary outcomes:

- provider and storage foundations stay reliable
- observability and replay remain strong enough to support later operator workflows
- shared contracts and composition patterns stay stable

### Phase 1: Workstation Core

Objective: turn the current workspace taxonomy into a real front-office operating shell.

Primary outcomes:

- durable `Research`, `Trading`, `Data Operations`, and `Governance` shells
- shared run browser/detail/portfolio/ledger flows, including filter recovery for already-loaded run-browser rows
- backtest, paper, and later live history converge toward one mental model

### Phase 2: Front Office and Control Plane

Objective: make Meridian credible for implementation and trade-support workflows.

Primary outcomes:

- stronger portfolio construction and implementation UX
- order/execution/trade-management workflows
- account and entity management
- Security Master productization

### Phase 3: Accounting and Treasury Core

Objective: establish real fund-accounting and cash-governance workflows.

Primary outcomes:

- multi-ledger structures
- trial balance and account-summary views
- cash-flow modeling and liquidity analysis

### Phase 4: Reconciliation and Governance Operations

Objective: make Meridian reliable for day-to-day middle- and back-office control workflows.

Primary outcomes:

- reconciliation engine
- break queues and exception workflows
- NAV and attribution support
- governance issue tracking

### Phase 5: Reporting and Stakeholder Outputs

Objective: turn platform data and controls into governed reporting outputs.

Primary outcomes:

- investor reporting packs
- board and management reporting
- compliance and audit-oriented outputs
- repeatable report-generation workflows

### Phase 6: Full Fund Operating Platform

Objective: complete the end-to-end lifecycle with stronger controls, approvals, and live-operations readiness.

Primary outcomes:

- connected research-to-implementation-to-operations lifecycle
- role-appropriate control points and approvals
- stronger live-operating readiness and policy enforcement

## 7. Current Repo Alignment

The current repository already supports parts of the target vision:

- ingestion, replay, storage, diagnostics, and export are in place
- shared run, portfolio, and ledger read services already exist
- WPF exposes a four-workspace shell baseline, run/portfolio/ledger/cash-flow drill-ins, Governance/Fund Ops routes, Security Master workflows with search recovery, Fund Accounts account/provider-routing briefing, Trading/Research/Data Operations briefing surfaces, Provider Health posture, System Health triage with pending-scan versus confirmed-empty guidance, Notification Center filter recovery, Activity Log triage, Watchlist posture, StrategyRuns filter recovery, BatchBacktest results empty guidance, QuantScript local run-history handoffs, and route-aware account-scoped operator-inbox navigation
- Security Master has contracts, services, storage, migrations, F# domain anchors, WPF drill-ins, conflict handling, corporate actions, trading parameters, and shared coverage/provenance propagation
- fund-structure, account/entity, ledger-group, cash-flow, and governed report-pack seams are in code, with focused tests around fund structure, report-pack schema/version behavior, and Fund Operations projections
- reconciliation now includes a file-backed break queue with review, resolve/dismiss, audit-history routes, and seeded exception-route/tolerance/sign-off metadata, while broader external-account/custodian matching and operator-approved calibrated casework remain open
- export infrastructure already supports JSONL, Parquet, Arrow, XLSX, and CSV

The main gap is operator-readiness and workflow cohesion, not a total absence of technical foundation.

## 8. Primary Implementation Tracks

The highest-value tracks from the current state are:

1. Complete the workstation shell and shared run model
2. Productize Security Master and account/entity concepts
3. Add trade-management and implementation workflows
4. Add multi-ledger, trial-balance, and cash-flow capabilities
5. Add reconciliation and governance exception workflows
6. Add governed reporting and investor-output tooling

## 9. Definition of Success

Meridian should be considered successful against this vision when:

- a fund team can use one product for research, implementation, operations, accounting support, and reporting workflows
- core objects remain consistent across front, middle, and back office surfaces
- reconciliation, accounting, and reporting workflows are first-class product experiences rather than ad hoc exports
- the platform remains self-hosted, auditable, and operationally understandable

## 10. Related Documents

- [Project Roadmap](../status/ROADMAP.md)
- [Feature Inventory](../status/FEATURE_INVENTORY.md)
- [Improvements Tracker](../status/IMPROVEMENTS.md)
- [Trading Workstation Migration Blueprint](trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](governance-fund-ops-blueprint.md)

## 11. Recommended Next Steps (2026-04-27 Snapshot)

These next steps keep the vision matrix aligned with the canonical Waves 2-4 path and the DK1/DK2 readiness wrapper.

### Wave A: close Wave 2 cockpit acceptance evidence

1. Keep DK1 provider-trust parity, explainability, calibration, and operator sign-off visible in the trading readiness lane.
2. Prove paper-session create, restore, replay, audit/control, and promotion-review continuity through repo-backed operator scenarios.
3. Keep WPF Trading, Research, Data Operations, Provider Health, System Health triage, Notification Center filter recovery, Activity Log triage, Watchlist posture, and StrategyRuns recovery surfaces as consumers of shared readiness/run/provider/diagnostics/history/log/symbol-state contracts, not separate acceptance models.

**Exit criteria**

- `Backtest -> Paper` can be exercised as one auditable operator path.
- Replay, signed DK1 trust posture, promotion checklist state, and operator work items are visible from the shared readiness contract and initial operator-inbox endpoint, with route-aware account-scoped WPF shell queue-button routing for the primary work item.
- The WPF shell supports the workflow without introducing shell-local readiness semantics.

### Wave B: deepen shared run / portfolio / ledger continuity

1. Make run continuity, parent/child promotion lineage, fills, portfolio, ledger, cash-flow, and reconciliation warnings visible through shared services.
2. Keep brokerage/custodian sync work feeding fund-account, portfolio, ledger, and reconciliation seams rather than parallel read models.
3. Keep the compatibility matrix and contract-review cadence active for shared DTO changes.

**Exit criteria**

- Research, Trading, and Governance drill-ins use the same run-centered continuity seam.
- Portfolio, ledger, cash-flow, and reconciliation gaps are explicit instead of silent partial views.
- Shared contract changes remain migration-aware and reviewable.

### Wave C: productize governance and fund operations

1. Extend the delivered file-backed reconciliation break queue into calibrated, durable casework.
2. Turn governed report packs from generated artifacts into reviewed, approved, and publishable outputs with provenance.
3. Connect account/entity, cash-flow, multi-ledger, reconciliation, and reporting views through the shared Fund Operations projection path, keeping Fund Accounts on shared account/provider-route/shared-data evidence.

**Exit criteria**

- Governance operators can review, assign, resolve, and audit exceptions durably.
- Report-pack history and artifacts are governed outputs, not only previews.
- Fund Operations can answer account, cash, reconciliation, and reporting posture from one shared query path.

### Cross-wave guardrails

- Keep **contract-first sequencing**: merge shared DTO/read-model slices before dependent UI surfaces.
- Treat **WPF + shared endpoint parity** as a release gate for each wave.
- Maintain **evidence and auditability** by preserving replay/export/diagnostics support in every phase.
- Run a **monthly roadmap reconciliation** between this matrix, the implementation backlog, the DK readiness dashboard, and the PR-sequenced roadmap to avoid phase drift.
