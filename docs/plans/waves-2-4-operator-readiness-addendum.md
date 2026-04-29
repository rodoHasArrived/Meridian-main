# Waves 2-4 Operator Readiness Addendum

**Owner:** Core Team
**Audience:** Product, Architecture, Desktop, API, Execution, Governance, and Platform contributors
**Last Updated:** 2026-04-29
**Status:** Active addendum - converts the canonical Waves 2-4 roadmap into concrete workstreams with ownership lanes, dependency rules, and exit criteria

---

## Summary

This addendum sharpens Meridian's active Waves 2-4 path without changing the canonical roadmap shape in [`../status/ROADMAP.md`](../status/ROADMAP.md).

The repo already has the right platform baseline and many of the right read-side seams:

- `src/Meridian.Execution/Services/PaperSessionPersistenceService.cs`
- `src/Meridian.Execution/Services/ExecutionAuditTrailService.cs`
- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- `src/Meridian.Strategies/Services/StrategyRunContinuityService.cs`
- `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`
- `src/Meridian.Application/Services/ReconciliationEngineService.cs`
- `src/Meridian.Application/Services/GovernanceExceptionService.cs`
- `src/Meridian.Application/Services/ReportGenerationService.cs`
- `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs`
- `src/Meridian.Wpf/Services/WorkspaceShellContextService.cs`

The remaining delivery gap is not "add more primitives." The remaining gap is durable operator control:

- turn paper-trading visibility into restart-safe, auditable workflow control
- make shared run continuity the system of record across workspaces
- land brokerage and custodian sync as a first-class execution and fund-account seam
- make governance casework and governed outputs durable instead of endpoint-local or preview-only

The commercial product thesis for these workstreams is the **Meridian Assurance Loop**: Data Trust Passport -> Run Evidence Graph -> Promotion Passport -> Accounting-Grade Paper Trading -> Governed Report Pack. Treat that as a prioritization filter for Waves 2-4, not as a new wave or a claim that the named modules are already complete.

The accounting-led commercial wedge asks whether Meridian can prove, book, reconcile, approve, and report an investment decision. Books Before Broker, Transaction Lab, Close the Books, broker statement reconciliation, Controls-as-Code, buyer demo mode, role-based demo views, readiness dashboards, and evidence packet actions belong in this addendum only as planned module direction until shared contracts, retained evidence, browser-dashboard workflows, and governed outputs prove a narrower slice.

This document therefore adds:

1. explicit owner lanes
2. per-wave workstreams
3. concrete dependencies
4. wave and workstream exit criteria

**Current-state refresh (2026-04-27):** the WPF shell/navigation baseline is now present enough to validate as a workflow consumer rather than planned shell plumbing, the former dashboard landing is standardized around `ResearchShell`, desktop launch/deep-link parsing now routes into canonical workspace tags, fixture mode is presented as neutral demo data, workflow automation now confirms page state through a hidden-but-present shell marker, isolated restore/build behavior avoids target-framework asset drift, local single-instance mutex and launch-argument forwarding behavior have focused coverage, the Trading shell now includes a desk briefing hero driven by active-run, workflow-summary, shared operator-readiness state, and stale replay count detail, Trading Hours now projects live-risk, pre-market staging, after-hours review, closed-planning session briefings, and holiday-calendar empty-state guidance from market-calendar state, OrderBook now projects depth, tape, spread, cumulative delta, and bid/ask pressure into one order-flow posture handoff, Data Operations now has a desk briefing hero for provider/backfill/storage/session/export handoffs, Provider Health now includes a provider-posture briefing for stale snapshots, disconnected streaming sessions, mixed-provider states, and blocked backfill coverage, System Health now includes a diagnostics triage briefing for provider, storage, and retained event posture plus pending-scan versus confirmed-empty guidance, Notification Center now recovers retained history when search, unread-only, or severity filters hide it, Activity Log now includes triage posture for visible entries, retained errors/warnings, latest activity, active filters, and export/clear command state, Watchlist now includes symbol-set posture for saved lists, pinned lists, search scope, symbol coverage, pinned-first ordering, and pinned badges, Messaging Hub now projects delivery posture, refresh recency, and retained activity scope from the view model, StrategyRuns now recovers retained run rows hidden by search or mode filters while showing visible-versus-recorded scope and comparison guidance, BatchBacktest now exposes stateful results empty guidance, the shared run review packet now emits stable route-aware work items for promotion, Security Master, continuity, and brokerage blockers, QuantScript now exposes local Run History with Research handoffs for mirrored runs, Fund Accounts now projects fund-context, account-queue, provider-routing, blocked-route, shared-data-access, balance-evidence snapshot posture, and ready-for-reconciliation states from loaded account/provider/balance-history evidence, the Wave 1 trust gate remains closed, and the 2026-04-27 DK1 pilot parity packet is signed with valid packet binding, validated samples, validated evidence documents, and validated explainability/calibration contracts. DK1 state and risk/control audit explainability are now projected into the trading readiness lane, the initial operator-inbox endpoint aggregates readiness and reconciliation work items with navigation targets, the WPF shell queue button consumes that endpoint, propagates active account context as `fundAccountId`, routes brokerage-sync blockers into `AccountPortfolio`, routes the primary work item, and can surface active review attention in shell context, the first governed report-pack artifact path exists, and the first file-backed reconciliation break-queue slice now supports seeded run-scoped breaks, assignment, resolve/dismiss, audit history, exception routing, tolerance profiles, sign-off status, and calibration-summary rollups. Wave 4 governance readiness remains dependent on generalized durable casework, operator-approved tolerance/severity calibration, external-account acceptance, governed output publication controls, end-to-end operator queue acceptance, and shared projection paths.

Current shell-support evidence also includes Welcome readiness progress for provider/symbol/storage checks, Storage archive posture for growth/capacity/last-scan handoffs, Storage preview scope/guidance for archive-path decisions, OrderBook order-flow posture for depth/tape/spread monitoring, compact shared deep-page command chrome with related-workflow and trust-state context, actionable shell-context attention detail, provider-degradation workflow summaries that route to `ProviderHealth`, brokerage-sync queue routing into `AccountPortfolio`, actionable latest-run review-packet work items in the shared operator inbox, and Trading hero attention states for warning or critical shared work items. The WPF screenshot-refresh workflow now provides scheduled/push/manual catalog and manual capture groups, per-workflow diagnostics, and a single post-matrix screenshot commit step. Keep this evidence in the workflow-validation lane; it does not replace Wave 2 cockpit, Wave 3 continuity, or Wave 4 governance exit criteria.

It should be read with:

- [`trading-workstation-migration-blueprint.md`](trading-workstation-migration-blueprint.md)
- [`paper-trading-cockpit-reliability-sprint.md`](paper-trading-cockpit-reliability-sprint.md)
- [`brokerage-portfolio-sync-blueprint.md`](brokerage-portfolio-sync-blueprint.md)
- [`governance-fund-ops-blueprint.md`](governance-fund-ops-blueprint.md)
- [`meridian-6-week-roadmap.md`](meridian-6-week-roadmap.md)

---

## Scope

### In scope

- concrete ownership and dependency mapping for active Waves 2-4
- workstreams that turn existing read models and operator surfaces into durable workflows
- cross-wave validation and release disciplines required to keep the active roadmap credible
- browser-dashboard delivery targets for readiness dashboards, close workflow previews, evidence packet actions, buyer demo mode, and role-based demo views after shared contracts exist

### Out of scope

- changing the canonical wave order
- broad Wave 5 Backtest Studio unification work
- broad Wave 6 live-readiness expansion
- optional advanced research, scale-out, or performance tracks
- claims that Books Before Broker, insurance accounting, Close the Books, broker statement reconciliation, Controls-as-Code, shadow books, or demo packs are implemented before accepted evidence exists

### Assumptions

- Owner labels below are responsibility lanes, not claims about a fixed org chart.
- A single individual or pair can own more than one lane in practice.
- Wave 1 remains the trust boundary for all downstream operator-readiness claims.

---

## Ownership Model

### Shared Workflow and Contracts

- Owns shared run, continuity, workstation contracts, promotion/read-model seams, and endpoint-to-service extraction.
- Primary repo anchors: `src/Meridian.Strategies/Services/`, `src/Meridian.Contracts/Workstation/`, `src/Meridian.Ui.Shared/Endpoints/`.

### Execution and Fund Accounts

- Owns paper-session reliability, OMS and audit behavior, brokerage integration seams, and fund-account sync ingestion.
- Primary repo anchors: `src/Meridian.Execution/`, `src/Meridian.Execution.Sdk/`, `src/Meridian.Application/FundAccounts/`, `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs`.

### Governance and Ledger

- Owns reconciliation, exception handling, cash-flow and reporting workflows, and fund-operations projections.
- Primary repo anchors: `src/Meridian.Application/Services/`, `src/Meridian.FSharp.Ledger/`, `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`.

### Workstation Shell and UX

- Owns browser-dashboard navigation and operator interaction patterns for new UI work, plus retained WPF shell consumption, operating-context switching, notification center, command palette, and workflow deep-linking where shared-contract regression coverage requires it.
- Primary repo anchors: `src/Meridian.Ui/dashboard/`, `src/Meridian.Ui.Shared/Endpoints/`, `src/Meridian.Wpf/Models/`, `src/Meridian.Wpf/ViewModels/`, `src/Meridian.Wpf/Services/`.

### Data Confidence and Validation

- Owns Wave 1 trust-gate maintenance, scenario-based acceptance harnesses, and evidence/doc synchronization.
- Primary repo anchors: `scripts/dev/run-wave1-provider-validation.ps1`, `docs/status/provider-validation-matrix.md`, generated `artifacts/provider-validation/` run outputs, `tests/`.

---

## Cross-Wave Dependency Rules

1. Wave 1 trust-gate evidence stays green before any Wave 2-4 readiness claim is promoted.
2. Wave 2 durable promotion traceability is a prerequisite for Wave 3 continuity and Wave 4 governance handoff.
3. Wave 3 shared run continuity must become the system of record before any client-specific shell work is allowed to widen; new operator UI scope belongs in the browser dashboard while WPF remains retained support.
4. Brokerage and custodian sync must land through execution and fund-account seams before governance can claim account-freshness or external-state continuity.
5. Governance casework must move out of endpoint-local or in-memory storage before report publishing and exception-SLA claims are treated as durable.
6. Accounting-led commercial slices must start with shared contract/readiness definitions such as accounting-impact previews, close checklist/readiness, statement-import reconciliation cases, Security Master confidence, report restatement tracking, controls-policy summaries, and evidence packet readiness before web dashboard workflow expansion.
7. Fallback and fixture payloads may remain for local development, but they are not acceptable as the operator path for wave exit criteria.
8. Desktop workflow automation must use canonical workspace tags, confirmed page-readiness markers, deterministic restore/build inputs, local single-instance behavior, diagnostic artifacts, and single-commit screenshot publication so screenshot/manual evidence does not drift from the shell routes operators actually use.
9. Operator-inbox run review-packet aggregation must stay bounded to actionable warning/critical latest-run blockers so the queue remains a triage surface, not a second run browser.

---

## Wave 2 Addendum

### Objective

Turn the current paper-trading cockpit from "visible" into "dependable" by making session continuity, replay proof, promotion review, and operator triage durable and explainable.

### W2-A: Paper Cockpit Reliability Gate

- **Primary owner:** Execution and Fund Accounts
- **Supporting owners:** Shared Workflow and Contracts; Workstation Shell and UX; Data Confidence and Validation
- **Depends on:** closed Wave 1 provider-confidence gate; current paper-session persistence and audit seams
- **Repo anchors:** `src/Meridian.Execution/Services/PaperSessionPersistenceService.cs`, `src/Meridian.Execution/Services/ExecutionAuditTrailService.cs`, `src/Meridian.Execution/OrderManagementSystem.cs`, `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs`, `src/Meridian.Ui/dashboard/src/screens/trading-screen.tsx`, `docs/plans/paper-trading-cockpit-reliability-sprint.md`
- **Deliverables:**
  - operator-visible replay verification gate
  - restart-safe session continuity proof
  - risk and control explainability from audited evidence
  - WPF Trading desk briefing states that consume shared readiness/replay/control/trust-gate posture without creating a separate acceptance model
  - cockpit acceptance checklist grounded in service behavior rather than UI surface count
- **Exit criteria:**
  - operators can create, restore, verify, and close a paper session without losing symbol scope, order history, or ledger continuity
  - replay mismatch states surface operator-readable reasons rather than only pass/fail
  - audited control or risk outcomes can be explained from cockpit-visible evidence
  - repo-backed tests prove restart, replay, and audit continuity for the operator lane

### W2-B: Durable Promotion Trace and Review Packet

- **Primary owner:** Shared Workflow and Contracts
- **Supporting owners:** Execution and Fund Accounts; Governance and Ledger; Workstation Shell and UX
- **Depends on:** W2-A session and audit reliability; existing `PromotionService` and run read models
- **Repo anchors:** `src/Meridian.Strategies/Services/PromotionService.cs`, `src/Meridian.Strategies/Models/StrategyRunEntry.cs`, `src/Meridian.Strategies/Services/StrategyRunReadService.cs`, `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- **Deliverables:**
  - durable promotion-history storage
  - explicit approver, approval reason, override, and audit-reference capture
  - one review-packet projection that bundles run summary, continuity status, fills, attribution, ledger, and audit references
- **Exit criteria:**
  - every `Backtest -> Paper` approval can be reconstructed after restart from source run to target run
  - promotion review surfaces show approver, reason, state transition, and audit linkage together
  - review packet DTOs are shared across research, trading, and governance consumers instead of being rebuilt per surface

### W2-C: Operator Triage Queue v1

- **Primary owner:** Workstation Shell and UX
- **Supporting owners:** Shared Workflow and Contracts; Governance and Ledger
- **Depends on:** W2-B durable promotion state; current shell context and notification center
- **Repo anchors:** `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs`, `src/Meridian.Wpf/Services/WorkstationOperatorInboxApiClient.cs`, `src/Meridian.Wpf/Services/WorkspaceShellContextService.cs`, `src/Meridian.Wpf/ViewModels/NotificationCenterViewModel.cs`, `src/Meridian.Ui.Services/Services/NotificationServiceBase.cs`
- **Deliverables:**
  - shared operator-work-item projection for promotion review, failed runs, and security/continuity gaps
  - `GET /api/workstation/operator/inbox` as the first shared queue endpoint, seeded by trading readiness work items plus open or in-review reconciliation breaks with stable IDs and navigation hints
  - shell queue-button counts, tone, and primary-item navigation driven by pending work items instead of mixed one-off counters
  - notification-center deep links into the underlying workflow surface, building on retained-history filter recovery rather than one-off toast text
- **Exit criteria:**
  - research and trading surfaces expose the same pending-review counts
  - at least promotion review, failed runs, continuity/security issues, and reconciliation breaks open from one operator queue
  - no critical Wave 2 action exists only as static copy or one-off toast text

### Wave 2 Exit Criteria

- paper trading is restart-safe, replay-verifiable, and auditable from the operator surface
- promotion review is durable and reconstructable after restart
- operator triage is visible through one shared queue model rather than fragmented alerts
- Wave 2 claims remain bounded to paper readiness and do not widen live-readiness language

---

## Wave 3 Addendum

### Objective

Make the shared run, portfolio, ledger, cash-flow, and reconciliation model feel like one system across research, trading, governance, the active browser dashboard, and retained WPF support surfaces.

Current WPF evidence now includes RunCashFlow guidance for selected-run, missing-run, no-event, and loaded retained cash-flow summaries. Keep that in the Wave 3 continuity lane; governance-wide cash-flow projections and reporting still belong to Wave 4.

### W3-A: Shared Run Continuity as System of Record

- **Primary owner:** Shared Workflow and Contracts
- **Supporting owners:** Governance and Ledger; Workstation Shell and UX
- **Depends on:** W2-B durable promotion trace; current shared run and continuity services
- **Repo anchors:** `src/Meridian.Strategies/Services/StrategyRunReadService.cs`, `src/Meridian.Strategies/Services/PortfolioReadService.cs`, `src/Meridian.Strategies/Services/LedgerReadService.cs`, `src/Meridian.Strategies/Services/CashFlowProjectionService.cs`, `src/Meridian.Strategies/Services/ReconciliationRunService.cs`, `src/Meridian.Strategies/Services/StrategyRunContinuityService.cs`
- **Deliverables:**
  - run continuity as the primary cross-workspace drill-in
  - standardized lineage, continuity-warning, and handoff payloads
  - explicit missing-data and timing-drift warnings instead of silent partial views
- **Exit criteria:**
  - research, trading, and governance drill-ins all route through the same run-continuity seam
  - parent/child promotion lineage is visible as part of run context, not a separate concept
  - portfolio, ledger, cash-flow, and reconciliation gaps show explicit continuity warnings

### W3-B: Projection-Layer Extraction and Consumer Alignment

- **Primary owner:** Shared Workflow and Contracts
- **Supporting owners:** Workstation Shell and UX
- **Depends on:** W3-A continuity DTO stabilization
- **Repo anchors:** `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`, `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`, `src/Meridian.Wpf/Models/ShellNavigationCatalog.cs`, `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs`
- **Deliverables:**
  - typed workspace read services for the core operator surfaces
  - clear separation between real operator payload builders and fixture/fallback payload builders
  - shared counts and summaries between WPF shell context and retained workstation API
- **Exit criteria:**
  - the core workspace payload path is service-backed and testable rather than endpoint-local composition
  - fallback payloads remain dev-only and are not confused with production operator readiness
  - WPF shell context, related-workflow routing, and API summaries agree on active workflow state
  - fixture/demo-mode payloads remain visibly non-operational and cannot satisfy readiness gates

### W3-C: Brokerage and Custodian Sync Control Plane

- **Primary owner:** Execution and Fund Accounts
- **Supporting owners:** Governance and Ledger; Shared Workflow and Contracts
- **Depends on:** Wave 1 bounded-provider discipline; W3-A shared continuity model
- **Repo anchors:** `src/Meridian.Execution.Sdk/IBrokeragePositionSync.cs`, `src/Meridian.Execution/Services/PositionReconciliationService.cs`, `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs`, `docs/plans/brokerage-portfolio-sync-blueprint.md`
- **Deliverables:**
  - account-catalog, portfolio-sync, and activity-sync interfaces
  - cursor and raw-snapshot storage
  - normalized account-state projections and freshness/divergence endpoints
  - explicit account-link workflow between broker-native accounts and Meridian fund accounts
- **Exit criteria:**
  - a linked external account can be discovered, synced, and inspected for freshness and divergence posture
  - raw brokerage snapshots and normalized projections are durably separated for auditability
  - synced external state feeds the same fund-account, portfolio, ledger, and reconciliation seams rather than a parallel stack

### W3-D: Structure-Aware Operating Context and Shell Handoff

- **Primary owner:** Workstation Shell and UX
- **Supporting owners:** Shared Workflow and Contracts; Governance and Ledger
- **Depends on:** W3-A continuity system of record; W3-B service-backed workspace payloads
- **Repo anchors:** `src/Meridian.Wpf/Services/WorkstationOperatingContextService.cs`, `src/Meridian.Wpf/Services/WorkspaceShellContextService.cs`, `src/Meridian.Wpf/Models/ShellNavigationCatalog.cs`, `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs`
- **Deliverables:**
  - consistent fund, entity, sleeve, vehicle, and account context propagation
  - shell deep-link and related-workflow routing that preserves operating scope
  - desktop launch/deep-link argument handling, single-instance argument forwarding, automation page-tag confirmation, and local activity triage tied to canonical workspace routes
  - shell-status badges tied to the selected operating context
- **Exit criteria:**
  - operators can change scope without losing the active run or account handoff context
  - related-workflow links and shell badges reflect the same selected operating context
  - WPF shell work remains a consumer of shared seams rather than introducing page-local workflow logic

### Wave 3 Exit Criteria

- strategy run continuity becomes Meridian's primary cross-workspace product seam
- external brokerage and custodian state is ingestible through execution and fund-account boundaries
- WPF shell context and retained workstation API are aligned around shared read services
- portfolio, ledger, cash-flow, and reconciliation are easier to follow as one operator story

---

## Wave 4 Addendum

### Objective

Finish governance and fund-operations productization by making casework, report publishing, and fund-operations review durable, reviewable, and operator-owned.

### W4-A: Durable Governance Casework

- **Primary owner:** Governance and Ledger
- **Supporting owners:** Shared Workflow and Contracts; Workstation Shell and UX
- **Depends on:** W3-A continuity system of record; W3-C synced external-state inputs
- **Repo anchors:** `src/Meridian.Application/Services/ReconciliationEngineService.cs`, `src/Meridian.Application/Services/GovernanceExceptionService.cs`, `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs`, `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- **Deliverables:**
  - persistent governance-exception repository
  - expand the delivered file-backed reconciliation break queue beyond run-scoped seeded breaks
  - assignment, notes, reopen, dismiss, resolve, and SLA timestamps
  - removal of endpoint-local or fixture break-queue state as the source of truth
- **Exit criteria:**
  - reconciliation breaks survive restart with stable identifiers and state history
  - governance operators can assign, review, resolve, dismiss, and reopen exceptions durably
  - casework queue counts are service-backed rather than held in a static endpoint-local dictionary

### W4-B: Governed Report-Pack Generation and Publishing

- **Primary owner:** Governance and Ledger
- **Supporting owners:** Shared Workflow and Contracts; Execution and Fund Accounts
- **Depends on:** W4-A durable casework; W3-C synced account-state provenance where relevant
- **Repo anchors:** `src/Meridian.Application/Services/ReportGenerationService.cs`, `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`, `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs`, `src/Meridian.Storage/Export/`
- **Deliverables:**
  - persisted report-pack records, not only previews
  - readiness checks and approval states before publication
  - provenance links back to ledger, reconciliation, and account-state inputs
- **Exit criteria:**
  - a report pack can be generated, reviewed, approved or rejected, regenerated, and exported with durable history
  - governance reporting moves beyond static profile visibility into governed-output workflow control
  - published outputs can name the source ledger, reconciliation, and account-state evidence used

### W4-C: Fund-Operations Workspace Consolidation

- **Primary owner:** Governance and Ledger
- **Supporting owners:** Execution and Fund Accounts; Workstation Shell and UX
- **Depends on:** W4-A casework; W4-B report publishing; W3-C brokerage and custodian sync
- **Repo anchors:** `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`, `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs`, `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs`, `docs/plans/governance-fund-ops-blueprint.md`
- **Deliverables:**
  - one shared fund-operations workspace projection for accounts, bank snapshots, brokerage freshness, cash ladders, reconciliation posture, NAV, and reporting
  - explicit account/entity/strategy structure drill-ins on the shared DTO path
  - Fund Accounts operator and balance-evidence briefing that stays aligned with shared account, provider-routing, retained balance-history, and shared-data-access projections
  - removal of duplicated or page-local fund-ops posture builders where shared services can own the projection
- **Exit criteria:**
  - governance shells can answer account posture, cash posture, reconciliation posture, and reporting posture from one shared query path
  - external account freshness and break posture are visible next to internal ledger and bank evidence
  - fund-operations review no longer depends on parallel per-screen read-model assembly

### W4-D: Operator Inbox v2 and Remediation Playbooks

- **Primary owner:** Workstation Shell and UX
- **Supporting owners:** Governance and Ledger; Execution and Fund Accounts
- **Depends on:** W2-C triage queue foundation; W4-A casework; W4-B report publishing
- **Repo anchors:** `src/Meridian.Wpf/ViewModels/NotificationCenterViewModel.cs`, `src/Meridian.Wpf/Views/NotificationCenterPage.xaml`, `src/Meridian.Wpf/Services/WorkspaceShellContextService.cs`
- **Deliverables:**
  - one inbox view that groups promotion reviews, reconciliation breaks, sync failures, security gaps, and report approvals
  - per-item deep links, severity, assignee, and next-action guidance
  - remediation playbooks that point the operator at the real workflow surface rather than generic alert copy
- **Exit criteria:**
  - notification center operates as a workflow inbox, not only a generic notification history page
  - at least promotion review, reconciliation break handling, sync-failure follow-up, and report approval enter the same inbox surface
  - shell critical-item badges and inbox counts are derived from the same durable work-item model

### Wave 4 Exit Criteria

- governance casework is durable, assignable, and restart-safe
- report packs are governed outputs with approval and provenance, not only previews
- fund-operations posture is available through one shared projection path
- governance becomes an operator workflow with durable queues, review surfaces, and publishable outputs

---

## Cross-Wave Supporting Tracks

### X1: Scenario Acceptance Harness

- **Primary owner:** Data Confidence and Validation
- **Supporting owners:** all active wave owners
- **Depends on:** wave services and DTOs stabilizing enough to test end-to-end scenarios
- **Repo anchors:** `tests/`, `docs/status/provider-validation-matrix.md`, `docs/plans/paper-trading-cockpit-reliability-sprint.md`
- **Deliverables:**
  - named scenario suites for `Backtest -> Paper`, paper-session restore, promotion review, run continuity, brokerage divergence, reconciliation break review, and report publish
  - command matrix that maps each scenario to the narrowest useful validation command
  - Wave 4 evidence records that use a deterministic template requiring scenario name, fixture window, API assertions, workstation assertions, artifact location, and regression owner (tracked in [`../status/wave4-evidence-template.md`](../status/wave4-evidence-template.md))
- **Exit criteria:**
  - each active wave has at least one repo-backed scenario suite tied to its own exit criteria
  - regression checks describe operator behavior, not only isolated method coverage

### Wave 4 Evidence Template Baseline (Applied)

The Wave 4 evidence template is now active in [`../status/wave4-evidence-template.md`](../status/wave4-evidence-template.md) and is seeded with the first three deterministic governance scenarios:

1. `wave4-governance-identifier-conflict-resolution-v1`
2. `wave4-governance-corporate-action-propagation-impact-v1`
3. `wave4-governance-multi-ledger-reconciliation-break-classification-v1`

These records establish the minimum acceptance payload for Wave 4 readiness proof and should be extended (not bypassed) as additional governance scenarios are onboarded.

### X2: Wave 1 Trust-Gate Preservation

- **Primary owner:** Data Confidence and Validation
- **Supporting owners:** Execution and Fund Accounts
- **Depends on:** none beyond the existing closed gate
- **Repo anchors:** `scripts/dev/run-wave1-provider-validation.ps1`, `docs/status/provider-validation-matrix.md`, `docs/providers/provider-confidence-baseline.md`, generated `artifacts/provider-validation/` run outputs
- **Deliverables:**
  - synchronized evidence, docs, generated runtime outputs, and automation summary
  - contradiction checks whenever provider or checkpoint proof surfaces change
- **Exit criteria:**
  - Wave 2-4 documentation does not overclaim provider or brokerage readiness
  - trust-gate drift is caught before downstream roadmap language is refreshed

---

## Recommended Delivery Order

1. W2-A Paper Cockpit Reliability Gate
2. W2-B Durable Promotion Trace and Review Packet
3. X1 Scenario Acceptance Harness baseline for Wave 2
4. W2-C Operator Triage Queue v1
5. W3-A Shared Run Continuity as System of Record
6. W3-B Projection-Layer Extraction and Consumer Alignment
7. W3-C Brokerage and Custodian Sync Control Plane
8. W3-D Structure-Aware Operating Context and Shell Handoff
9. W4-A Durable Governance Casework
10. W4-B Governed Report-Pack Generation and Publishing
11. W4-C Fund-Operations Workspace Consolidation
12. W4-D Operator Inbox v2 and Remediation Playbooks

This ordering keeps the work honest:

- Wave 2 proves the operator lane.
- Wave 3 turns that lane into the shared system of record.
- Wave 4 builds durable governance workflows on top of the same seams.

