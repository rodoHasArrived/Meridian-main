# Meridian Fund Management Product Vision and Capability Matrix

**Owner:** Core Team
**Audience:** Product, architecture, engineering, operations, and stakeholders
**Last Updated:** 2026-03-24
**Status:** Active planning document

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
| Workspace shell (`Research`, `Trading`, `Data Operations`, `Governance`) | Cross-cutting platform | In progress | Phase 1 | Vocabulary and shell are in place; richer workflow UX pending |
| Shared run model across backtest, paper, and live history | Front office | In progress | Phase 1 | First WPF run browser/detail/portfolio/ledger flow exists |
| Strategy research and backtesting | Front office | Implemented baseline | Phase 1 | Needs stronger shared-run UX and comparison flows |
| Portfolio construction and implementation | Front office | In progress | Phase 2 | Needs stronger operator workflow, approvals, and cockpit UX |
| Order, execution, and trade management | Front office / Middle office | Planned | Phase 2 | Expand paper-trading and execution surfaces into real trade-management workflows |
| Account and entity management | Middle office / Back office | Planned | Phase 2 | Introduce fund, sleeve, vehicle, account, and entity views |
| Security Master productization | Middle office / Cross-cutting platform | Implemented baseline | Phase 2 | Backend anchors exist; workstation-facing workflows still pending |
| Position and portfolio oversight | Middle office | In progress | Phase 2 | Shared read models exist; broader operator tooling pending |
| Multi-ledger accounting | Back office | Planned | Phase 3 | Requires ledger grouping, consolidation, and entity-aware views |
| Trial balance and account-summary analysis | Back office | Planned | Phase 3 | Ledger read-model baseline exists; productization pending |
| Cash-flow modeling and liquidity views | Middle office / Back office | Planned | Phase 3 | Depends on Security Master and ledger enrichment |
| Reconciliation engine and break queues | Middle office / Back office | In progress | Phase 4 | Run-scoped reconciliation and a file-backed break queue exist; position, cash, custodian, and external-statement matching remain |
| NAV, attribution, and governance exception workflows | Middle office / Back office | Planned | Phase 4 | Built on shared portfolio, ledger, and pricing data |
| Investor, board, compliance, and fund-ops reporting | Back office | Planned | Phase 5 | Builds on export infrastructure plus governed report generation |
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
- shared run browser/detail/portfolio/ledger flows
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
- WPF already exposes initial workstation-facing run, portfolio, and ledger flows
- Security Master has contracts, services, storage, migrations, and F# domain anchors
- export infrastructure already supports JSONL, Parquet, Arrow, XLSX, and CSV

The main gap is productization and workflow cohesion, not a total absence of technical foundation.

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

## 11. Recommended Next Steps (2026-03-24 Snapshot)

These next steps convert this vision matrix into an executable near-term sequence, aligned with the PR-sequenced roadmap and module backlog.

### Wave A (next 2 weeks): lock the Phase 1 base

1. **Finish workspace shell hardening and bootstrap alignment** (`PR-01`, `PR-03`).
2. **Complete shared run contract expansion** with paper/live-compatible run fields (`PR-02`).
3. **Publish a Phase 1 acceptance checklist** for run browser/detail/portfolio/ledger parity across WPF and shared API contracts.

**Exit criteria**

- `Research`, `Trading`, `Data Operations`, and `Governance` shells are stable enough for day-to-day operator navigation.
- Shared run payloads no longer depend on placeholder bootstrap data.
- Run drill-ins are the canonical entry path for backtest and paper history.

### Wave B (following 2–4 weeks): establish Phase 2 control plane

1. **Start trade-management contract and cockpit baseline** (`PR-05`, `PR-11`).
2. **Productize Security Master** and merge projection enrichment (`PR-06`, `PR-08`).
3. **Introduce account/entity/fund-structure contracts** as a shared dependency for governance and accounting surfaces (`PR-07`).

**Exit criteria**

- Traders can move from research output to implementation intent with explicit order/fill/position status.
- Governance surfaces can read normalized instrument, account, and entity context.
- Security Master data is consumable as a product surface, not just backend storage.

### Wave C (next planning increment): open the Phase 3 accounting path

1. **Deliver multi-ledger kernel baseline** (`PR-09`).
2. **Add governance DTO and read-model expansion** needed by accounting views (`PR-10`).
3. **Ship trial balance and cash-flow operator surfaces** (`PR-12`).

**Exit criteria**

- Fund/entity/sleeve/account structures can be represented in ledger groupings.
- Governance workspace shows trial balance and cash/liquidity summaries from shared read models.
- Phase 4 reconciliation work can begin without redefining core accounting contracts.

### Cross-wave guardrails

- Keep **contract-first sequencing**: merge shared DTO/read-model slices before dependent UI surfaces.
- Treat **WPF + shared endpoint parity** as a release gate for each wave.
- Maintain **evidence and auditability** by preserving replay/export/diagnostics support in every phase.
- Run a **monthly roadmap reconciliation** between this matrix, the implementation backlog, and the PR-sequenced roadmap to avoid phase drift.
