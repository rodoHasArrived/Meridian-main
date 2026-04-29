# Meridian - Project Roadmap

**Last Updated:** 2026-04-29
**Status:** Active productization — the narrow Wave 1 trust gate is repo-closed, DK1 provider trust now has a signed 2026-04-27 pilot replay/sample-set parity packet, valid packet-bound operator sign-off, and cockpit readiness projection; Waves 2-4 remain the core operator-readiness path. New desktop feature development is paused, and the active operator UI lane is now the web dashboard in `src/Meridian.Ui/dashboard/` with built assets served from `src/Meridian.Ui/wwwroot/workstation/`. The WPF workspace-shell baseline remains retained support evidence for shared contracts, regression fixes, and compatibility checks.
**Repository Snapshot (2026-04-28 current repo):** solution/build/test project files: 43 | `src/` project files: 27 | test projects: 9 | workflow files: 40

Meridian is no longer primarily blocked on missing platform primitives. The repo already contains strong market-data, storage, replay, backtesting, execution, ledger, workstation, and Security Master foundations. The remaining delivery problem is now narrower and more product-shaped: prove operator trust, close workflow gaps, and deepen governance in one browser-first operator UI without letting the product split into parallel subsystems.

The active roadmap therefore centers on four outcomes:

- prove operator trust with evidence-backed provider, checkpoint, and replay validation
- harden the paper-trading cockpit through the active web workstation dashboard
- make shared run / portfolio / ledger continuity the default integration path across `Data`, `Strategy`, `Trading`, `Portfolio`, `Accounting`, and `Reporting`
- productize governance and fund-operations on top of the delivered Security Master baseline

Use this document with:

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md) - current capability status
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) - normalized non-assembly backlog
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md) - completed improvement history
- [`production-status.md`](production-status.md) - current readiness posture and provider-confidence gates
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) - prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) - concise end-state product summary
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) - shortest combined roadmap, opportunity, and target-state entry point
- [`../plans/meridian-pilot-workflow.md`](../plans/meridian-pilot-workflow.md) - golden-path pilot workflow and productization filter
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md) - workstation target state
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md) - governance target state
- [`../plans/brokerage-portfolio-sync-blueprint.md`](../plans/brokerage-portfolio-sync-blueprint.md) - external brokerage and custodian account-sync design
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) - current short-horizon execution plan
- [`../plans/waves-2-4-operator-readiness-addendum.md`](../plans/waves-2-4-operator-readiness-addendum.md) - concrete owner-based workstreams, dependencies, and exit criteria for the active Waves 2-4 path
- [`../plans/web-ui-development-pivot.md`](../plans/web-ui-development-pivot.md) - current web-first operator UI direction

---

## Canonical Program State

Program wave status is canonical in [`PROGRAM_STATE.md`](PROGRAM_STATE.md). Any wave status wording in this file is explanatory context only. Generated ownership and escalation routing for status summaries is published in [`program-state-summary.md`](program-state-summary.md).

<!-- program-state:begin -->
| Wave | Owner | Primary Owner | Backup Owner | Escalation SLA | Dependency Owners | Status | Target Date | Evidence Link |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| W1 | Data Operations + Provider Reliability | Data Confidence and Validation | Trading Workstation | 4 hours / 1 business day | Trading Workstation; Shared Platform Interop; Governance and Ledger | Done | 2026-04-17 | [`production-status.md#provider-evidence-summary`](production-status.md#provider-evidence-summary) |
| W2 | Trading Workstation | Execution and Fund Accounts | Workstation Shell and UX | 4 hours / 1 business day | Shared Workflow and Contracts; Data Confidence and Validation; Governance and Ledger | In Progress | 2026-05-29 | [`ROADMAP.md#wave-2-workstation-paper-trading-cockpit-completion`](ROADMAP.md#wave-2-workstation-paper-trading-cockpit-completion) |
| W3 | Shared Platform Interop | Shared Workflow and Contracts | Workstation Shell and UX | 1 business day / 2 business days | Execution and Fund Accounts; Governance and Ledger; Data Confidence and Validation | In Progress | 2026-06-26 | [`ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity`](ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity) |
| W4 | Governance + Fund Ops | Governance and Ledger | Shared Workflow and Contracts | 1 business day / 2 business days | Execution and Fund Accounts; Workstation Shell and UX; Shared Platform Interop | In Progress | 2026-07-24 | [`ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline`](ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline) |
| W5 | Research Platform | Strategy and Research | Shared Workflow and Contracts | 2 business days / 3 business days | Workstation Shell and UX; Data Confidence and Validation; Shared Platform Interop | Planned | 2026-08-21 | [`ROADMAP.md#wave-5-backtest-studio-unification`](ROADMAP.md#wave-5-backtest-studio-unification) |
| W6 | Execution + Brokerage Integrations | Execution and Brokerage Integrations | Governance and Ledger | 4 hours / 1 business day | Data Confidence and Validation; Shared Platform Interop; Workstation Shell and UX | Planned | 2026-09-18 | [`ROADMAP.md#wave-6-live-integration-readiness`](ROADMAP.md#wave-6-live-integration-readiness) |
<!-- program-state:end -->

---

## Summary

Meridian's platform foundations are already broad enough that roadmap priority should now come from operator value and readiness evidence, not from generalized platform sprawl. The repo already includes:

- a strong ingestion and storage baseline with bounded channels, WAL durability, JSONL and Parquet sinks, replay, backfill scheduling, gap analysis, packaging, lineage, and export
- shared workstation endpoints and a workstation model now expressed through the visible `Data`, `Strategy`, `Trading`, `Portfolio`, `Accounting`, `Reporting`, and `Settings` workspaces
- shared `StrategyRun`, portfolio, ledger, and reconciliation read paths in `src/Meridian.Strategies/Services/` and `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- execution, paper-trading, strategy lifecycle, and promotion seams, including wired `/api/execution/*`, `/api/promotion/*`, and `/api/strategies/*` surfaces
- a retained WPF workstation baseline with run-centered pages, Security Master drill-ins, and desktop shell modernization already landed
- a browser dashboard lane in `src/Meridian.Ui/dashboard/` that is now the active UI implementation path, including a React/Vite Research run library that can review retained runs, cap compare selection to two runs, call shared compare/diff/promotion-history APIs, surface command failures as operator-visible alerts, and ship built workstation assets from `src/Meridian.Ui/wwwroot/workstation/`
- a delivered Security Master platform seam with shared coverage and provenance flowing across research, trading, portfolio, ledger, reconciliation, governance, and WPF drill-ins

The meaningful repo delta since the April 8 planning refresh is no longer only WPF workflow consolidation. On 2026-04-29, new operator UI work pivots to the browser dashboard. Existing WPF evidence should be treated as retained support and shared-contract regression coverage, while new workflow value should be proven through web dashboard paths for cockpit, shared-model, data-operations, and governance use cases. The first web Research support slice is present as a run-library view model plus component coverage for two-run compare/diff readiness, selected-run copy, promotion-history loading state, accessible error alerts, and refreshed built workstation assets. The web shell also has a `buildAppShellViewState` seam for active workspace resolution, loading status, partial-bootstrap degradation, failed-slice retry copy, and full-bootstrap failure handling. These are support evidence for Wave 3 continuity and browser-shell resilience, not closure of Backtest Studio unification or Wave 2 cockpit acceptance.

The current shell baseline also now includes a first-run Welcome readiness progress strip for provider connection, symbol inventory, and storage-path posture; a Storage archive-posture card for daily growth, capacity horizon, last scan, and one archive handoff from the already-loaded analytics snapshot; a Storage preview scope strip that shows selected root, layout, compression, sample tree, and archive-path guidance before backfill/export/package work; an OrderBook posture strip that summarizes selected-symbol scope, depth availability, spread, cumulative delta, trade-tape readiness, and bid/ask pressure before an operator reads the ladder; compact shared deep-page command chrome that preserves related-workflow and trust-state context inside `WorkspaceDeepPageHostPage`; actionable shell-context attention detail with severity, owner, source, and next action; provider-degradation workflow summaries that route the next handoff to `ProviderHealth` instead of the generic provider page; and Trading desk hero plus main-shell queue logic that treats warning or critical shared `WorkItems` as first-class blockers and routes brokerage-sync work to `AccountPortfolio` before it can show a ready active-run state. Treat these as operator-orientation and routing-quality evidence, not as new wave exits.

The shared-run support evidence now also includes `RunCashFlowPage` empty-state guidance for selected-run, missing-run, no-event, and loaded cash-flow summaries. This tightens Wave 3 continuity around retained run evidence, but it does not close the broader governance cash-flow/projection work still listed under Wave 4.

The repo delta on 2026-04-28 is release-evidence hardening rather than a wave-exit change. `.github/workflows/refresh-screenshots.yml` now supports scheduled, push, and manual WPF screenshot capture with selectable catalog/manual workflow groups, least-privilege default permissions, per-workflow diagnostic artifacts, and a single downstream commit job so PNG updates are merged once after the matrix completes. The generated documentation dashboard also refreshed to an 89/100 health score and the generated coverage report shows 88.3% documented items; endpoint and configuration documentation gaps remain cleanup work, not operator-readiness blockers.

The DK1 evidence posture also sharpened after the prior roadmap snapshot. [`provider-validation-matrix.md`](provider-validation-matrix.md), [`dk1-pilot-parity-runbook.md`](dk1-pilot-parity-runbook.md), and [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md) now point to a `pilotReplaySampleSet` contract emitted by `scripts/dev/run-wave1-provider-validation.ps1`; the current repo also wires that validation run into `scripts/dev/generate-dk1-pilot-parity-packet.ps1` so the automation emits `dk1-pilot-parity-packet.json` and `.md` beside the Wave 1 summary. The current 2026-04-27 evidence set is present under `artifacts/provider-validation/_automation/2026-04-27/`: the parity packet is `ready-for-operator-review`, all four pilot sample rows are ready, all four evidence documents are validated, the trust-rationale and baseline-threshold contracts are validated, and the packet-bound sign-off file is signed by the Data Operations, Provider Reliability, and Trading owners with `operatorSignoff.validForDk1Exit=true`. Future DK1 reviews still need fresh date-stamped packets and matching packet-bound sign-off files; copied or stale sign-off files remain invalid. Shared interop readiness is **Ready for cadence monitoring** after the baseline `artifacts/contract-review/2026-04-27/contract-review-packet.*` packet and Shared Platform Interop owner approval locked the weekly Wednesday review cadence. The cockpit projects that DK1 packet through `Dk1TrustGateReadinessService`, `TradingTrustGateReadinessDto`, structured `OperatorSignoff`, `SampleReviews`, `EvidenceDocuments`, `TrustRationaleContract`, `BaselineThresholdContract`, and a `ProviderTrustGate` work item if a future packet lacks valid sign-off, so the readiness lane no longer has to infer DK1 state from documents alone. The promotion handoff lane has started in a narrow cockpit audit-feedback slice, the export DK2 lane is **Early In Progress** through governed report-pack schema/version checks, and the reconciliation/governance DK2 lane is now **Early In Progress** through a file-backed reconciliation break queue with review, resolve/dismiss, audit-history routes, seeded exception-route/tolerance/sign-off metadata, and `/api/workstation/reconciliation/calibration-summary` profile rollups for Ready/ReviewRequired/Blocked calibration posture. Durable generalized governance casework, operator-approved tolerance calibration, and Wave 2 cockpit handoff acceptance still remain open.

---

## Current State

Wave-by-wave status labels are tracked in the canonical table in [`PROGRAM_STATE.md`](PROGRAM_STATE.md).

For implementation detail and evidence, use:

- [`production-status.md`](production-status.md) for readiness posture and provider evidence
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md) for active execution tracks
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) for the shortest stakeholder summary

---

## What Is Complete

### Platform baseline

- Meridian's ingestion, storage, replay, export, and data-quality stack is no longer a major roadmap blocker.
- The repo has a credible archival and replay platform, broad provider coverage, and materially stronger operational-readiness foundations than earlier roadmap snapshots.
- The historical improvement backlog is effectively closed for the current platform baseline, which is a real milestone.

### Execution and workflow foundations

- The seven-workspace operator model is present in the current shell, with older four-workspace planning language retained only as compatibility grouping.
- The workstation surfaces contain material workflows for the visible `Data`, `Strategy`, `Trading`, `Portfolio`, `Accounting`, `Reporting`, and `Settings` workspaces rather than only navigation and summary surfaces; the browser dashboard is now the active operator UI shell, while WPF remains a retained compatibility shell and regression-support surface. The current web Research lane already exposes retained run review, two-run compare/diff readiness, promotion-history loading, and operator-visible command failure states through `ResearchScreen` and `useResearchRunLibraryViewModel`, while the web app shell now centralizes loading/degraded/failure route gating through `buildAppShellViewState`.
- The Wave 2 cockpit now has a shared readiness seam through `/api/workstation/trading/readiness` and `TradingOperatorReadinessDto`, joining paper-session state, replay verification, execution controls, DK1 trust-gate packet posture, promotion status, brokerage sync, acceptance gates, overall readiness, work items, and warnings into one operator-facing contract. The shared `/api/workstation/operator/inbox` endpoint now aggregates those readiness work items with actionable warning/critical run review-packet items from the latest runs, open/in-review reconciliation breaks, and navigation targets, and the WPF shell queue button consumes that endpoint to show review counts, severity tone, account-scoped brokerage/readiness blockers when an account operating context is active, route-aware primary-item navigation into concrete workbenches such as `FundReconciliation` and run review packets, and shell-context attention when a review item is active without closing the end-to-end operator-inbox workflow. Promotion approvals also have a canonical `PromotionApprovalChecklist` for DK1 trust-packet, run-lineage, portfolio/ledger-continuity, risk-control, and live-override review requirements. This is useful acceptance infrastructure, not a completed cockpit claim.
- The local Wave 2 replay-audit slice now strengthens that acceptance lane by recording replay consistency, compared fill/order/ledger counts, last-persisted timestamps, and primary mismatch reason into execution-audit metadata; the readiness gate also treats diverging active-session fill/order/ledger counts as stale replay coverage and drops back to review-required until replay verification is rerun. This improves restart and service-layer reconstruction evidence without closing the full cockpit-hardening gate.
- WPF already has meaningful run-centered workstation pages on top of the broader desktop page inventory, including a Position Blotter selection-review rail that summarizes grouped exposure, long/short/gross quantities, unsupported rows, and batch-action eligibility before an operator flattens or upsizes selected execution rows.
- The WPF shell/navigation baseline is materially delivered: four workspace shell pages, metadata-driven navigation, deep-page hosting, command/search metadata, context strips, canonical `ResearchShell` startup/deep-link handling, the Trading, Research, and Data Operations desk briefing heroes, and navigation/shell smoke tests are present. The Trading hero now projects active-run, workflow-summary, replay/readiness, controls, DK1 trust, brokerage-sync, stale replay count detail, and handoff state into the shell; `TradingHours` now projects live-risk, pre-market staging, after-hours review, closed-planning session briefings, and holiday-calendar empty-state guidance from market-calendar state before operators stage trading-desk work; `OrderBook` now projects symbol scope, bid/ask depth availability, spread, cumulative delta, tape readiness, and pressure into one order-flow posture handoff before the ladder and tape; the Research hero now projects market briefing, selected-run, run detail, portfolio, and promotion-review handoff state; the Data Operations hero now projects provider, backfill, storage, session, export, environment mode, and operational-handoff state through `DataOperationsWorkspacePresentationBuilder`; `ProviderHealth` now projects stale snapshots, disconnected streaming sessions, mixed-provider states, and blocked backfill coverage into one provider-posture briefing before the operator scans provider cards; `SystemHealth` now summarizes provider, storage, and retained event posture into one triage handoff before the operator scans diagnostics panels, and its provider/recent-event empty states now distinguish pending scans from confirmed empty snapshots; `NotificationCenter` now lets operators reset search, unread-only, and severity filters when retained notification history is hidden; `ActivityLog` now summarizes visible entries, retained error/warning counts, latest entry, and active filters before the operator scans individual log rows, and exposes export/clear header actions only when visible or retained log state supports them; `Watchlist` now summarizes saved lists, pinned lists, symbol coverage, visible search scope, and empty-state guidance before the operator loads or imports symbol sets, then orders pinned lists first with visible card badges so desk-ready symbol sets are easier to select; `MessagingHub` now projects message-flow posture, subscriber readiness, retained activity scope, bound refresh recency, and clear-activity command state from its view model; `StrategyRuns` now distinguishes an empty run library from filters that hide retained runs, keeps visible-versus-recorded run scope beside search, exposes reset-filters recovery without reloading the run store, and guides compare-run selection when visible run state cannot produce a valid pair; `BatchBacktest` now gives the sweep results pane stateful empty guidance for idle, validation-blocked, running, failed-without-results, cancelled, and populated states; `QuantScript` now exposes local execution history in-page with empty-state guidance plus run-browser, run-detail, and compare handoffs when a mirrored Strategy Run exists; `SecurityMaster` now exposes runtime-unavailable search recovery plus a bound `Clear Search` action so operators can reset no-match or unavailable-runtime search state without another workstation read; `FundAccounts` now projects fund-context, account-queue, provider-routing, blocked-route, shared-data-access, balance-evidence snapshot posture, and ready-for-reconciliation states from already-loaded account, route, provider, and balance-history evidence. Desktop workflow validation is also stronger because page-tag automation now uses an invisible-but-real `ShellAutomationState` element, isolated restore/build behavior avoids target-framework asset drift, local single-instance behavior has focused mutex and launch-argument forwarding coverage, and the GitHub screenshot refresh lane can capture catalog/manual WPF workflows with diagnostics before committing updated screenshots once. The remaining roadmap question is still whether those surfaces measurably improve active Wave 2-4 workflows.
- First-run, storage, context-strip, and deep-page shell polish now has concrete support evidence: `WelcomePageViewModel` summarizes readiness progress across connection, symbols, and storage; `StorageViewModel.BuildStoragePosture()` projects archive growth, capacity horizon, last-scan, empty-archive, capacity-warning, stable, and unavailable states from the loaded analytics snapshot; `StorageViewModel.RefreshPreview()` normalizes preview roots and projects layout/compression scope before archive-path decisions; `WorkspaceDeepPageHostPage` keeps compact command, related-workflow, and trust-state context around hosted pages; attention banners now include severity, owner, source, and action guidance; degraded provider workflow summaries open `ProviderHealth` as the next action; and the Trading hero blocks ready presentation when shared readiness work items still require attention. These sharpen operator orientation and routing but do not close cockpit, shared-model, or governance acceptance.

### Shared-model baseline

- `StrategyRunReadService`, `PortfolioReadService`, and `LedgerReadService` give Meridian a stable seam for unifying backtest, paper, live-aware, portfolio, and ledger views.
- Workstation endpoints already expose run comparison, diff, fills, attribution, ledger summaries, reconciliation, and Security Master read paths.

### Security Master baseline

- Security Master is no longer a blueprint-only seam. The WPF browser, workstation endpoints, shared security references, conflict handling, corporate actions, and trading-parameter flows are materially in code.
- Meridian now has one authoritative instrument-definition seam that already propagates into Research, Trading, Portfolio, Ledger, Reconciliation, Governance, and WPF drill-ins.

### Governance baseline

- Governance is no longer hypothetical. Security Master, Fund Accounts account-queue/routing posture, reconciliation, calibration-summary rollups, direct lending, export profiles, and governance-facing UI and API seams are real and discoverable in the repo.
- The product gap has shifted from "build governance foundations" to "finish governance productization and workflow continuity."

---

## What Remains

- **Wave 1 maintenance:** keep the closed provider-confidence, checkpoint, and Parquet evidence gate aligned around Alpaca, Robinhood, and Yahoo
- **DK1 execution:** preserve the signed 2026-04-27 provider-trust packet and rerun packet-bound review whenever provider evidence, trust rationale, or threshold calibration changes
- **Wave 2:** turn the current paper-trading cockpit from "visible" into "dependable," with acceptance evidence tied to DK1 trust signals
- **Wave 3:** make run history, portfolio, ledger, cash-flow, and reconciliation behave like one cross-workspace model under the shared compatibility matrix
- **Wave 4:** deepen governance and fund-operations workflows on top of the delivered Security Master baseline, including the new reconciliation calibration-summary rollups, then prove them through DK2 promotion/export/reconciliation gates
- **Wave 5:** unify native and Lean workflows into one Backtest Studio once the shared model is stable enough to support it cleanly
- **Wave 6:** expand into controlled live integration readiness only after trust and paper-workflow gates are materially closed
- **Optional:** pursue advanced research, simulation, scale-out, and performance tracks only after the core workstation product is coherent and trustworthy

---

## Target End Product

Meridian's target end state is a self-hosted browser-first trading workstation and fund-operations platform organized around the visible `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` workspaces.

`Data` establishes evidence-backed provider trust, `Strategy` turns that data into reviewed runs and comparisons, `Trading` promotes approved runs into paper workflows, `Portfolio` and `Accounting` review the resulting account, portfolio, ledger, cash-flow, and reconciliation evidence, and `Reporting` turns the governed evidence set into retained report packs.

The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context.

---

## Recommended Next Waves

Across Waves 2-4, keep web-dashboard workflow consolidation, validation coverage, route/deep-link behavior, fixture/demo-mode cues, and architecture simplification reinforcing the same read-model and orchestration seams rather than becoming a parallel delivery program. Use WPF validation only for retained desktop compatibility or shared-contract regression coverage.

### Wave 1: Closed provider confidence and checkpoint evidence gate

**Why now:** This gate is now closed in repo evidence and should be preserved as the trust boundary for every downstream readiness claim.

**Focus:**

- keep Alpaca provider and stable execution seam evidence explicit as the repo-closed core provider baseline
- keep Robinhood supported-surface evidence aligned with its bounded runtime artifact set without overstating live readiness
- formalize Yahoo as a historical-only core provider row backed by deterministic repo tests
- keep checkpoint reliability and Parquet L2 flush behavior on the passing suite list inside `run-wave1-provider-validation.ps1`
- treat the WPF Data Operations hero and Provider Health posture briefing as support evidence only when they keep provider, backfill, storage, session, and export handoffs tied to shared operational services rather than duplicating readiness logic
- keep the emitted DK1 `pilotReplaySampleSet`, generated `dk1-pilot-parity-packet.*` artifacts, and packet-bound operator sign-off template synchronized with the pilot parity runbook and provider-validation matrix; generated packets are date-stamped run outputs and must be regenerated or attached for review, not assumed from removed repo artifacts
- keep provider-confidence docs, deferred-provider language, runtime artifact folders, the validation matrix, and the latest automation summary synchronized with executable evidence

**Exit signal:** The Wave 1 matrix, roadmap, status docs, DK1 pilot runbook, generated parity packet, packet-bound sign-off template, and automation summary all describe the same active provider set and pilot replay/sample contract; Alpaca and Yahoo remain repo-closed, Robinhood remains explicitly bounded, checkpoint and L2 rows stay closed in repo tests, and deferred providers are not implied to be current blockers.

<a id="wave-2-web-paper-trading-cockpit-completion"></a>

### Wave 2: Workstation paper-trading cockpit completion

**Why now:** Meridian already has the execution, session, and promotion APIs. Product value now depends on finishing the operator cockpit.

**Focus:**

- tighten positions, orders, fills, replay, sessions, and risk workflows into a dependable operator lane
- keep promotion evaluation, approval, and rejection rationale explicitly tied to operator review, with outcome severity and history refresh behavior visible in the cockpit
- use the new trading-readiness contract as the acceptance surface for session state, replay consistency and freshness, audit/control evidence, risk/control explainability warnings, promotion review, DK1 trust-gate packet/sign-off posture, overall readiness, brokerage-sync posture, and stable operator work items that can be refreshed by the web cockpit and the shared operator-inbox endpoint without random ID churn; account operating contexts should preserve account-scoped brokerage/readiness blockers through `fundAccountId`, and run review-packet queue items should stay bounded to actionable warning/critical latest-run blockers
- validate the web Trading cockpit against context-required, replay-mismatch, controls-blocked, paper-review, and live-oversight operator states without treating retained WPF hero coverage as cockpit completion
- verify session persistence, replay behavior, and stale-replay recovery under realistic scenarios
- align cockpit behavior with brokerage-adapter and provider-confidence evidence

**Exit signal:** A strategy can move from backtest into a visible, auditable paper-trading workflow through the shared workstation contract, with the web dashboard and retained local API consuming the same readiness lane.

### Wave 3: Shared run / portfolio / ledger continuity

**Why now:** The contracts exist, but the product experience around them is not yet fully realized.

**Focus:**

- deepen run history and comparison depth across backtest, paper, and live-aware modes
- strengthen portfolio, attribution, fills, ledger, cash-flow, and reconciliation continuity
- land brokerage and custodian account-sync ingestion that feeds the same shared portfolio, ledger, and reconciliation seams
- grow the new file-backed reconciliation break queue and calibration-summary rollups beyond run-scoped seeded breaks and seeded exception-route/tolerance/sign-off metadata into operator-approved calibration, durable casework, and external-account/custodian review flows
- keep Security Master enrichment, the web Research run library, and retained WPF workflow work tied to the same shared read-model seam, including browser run pair selection for compare/diff, promotion-history loading/error states, the retained Research desk briefing hero's run-detail, portfolio, and paper-promotion review handoffs, StrategyRuns filter recovery on the already-loaded run browser rows, QuantScript run-history handoffs into shared Research views when mirrored runs exist, and stable route-aware review-packet work items for promotion, security coverage, continuity, and brokerage blockers

**Exit signal:** Strategy runs become Meridian's primary cross-workspace product object rather than one of several overlapping representations.

### Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

**Why now:** Governance is already visible in code, and Security Master is already the delivered authoritative instrument seam. This is now a workflow-deepening problem rather than a missing-foundation problem.

**Focus:**

- add account/entity and strategy-structure workflows on top of the existing governance baseline
- add multi-ledger, cash-flow, reconciliation, and reporting slices on top of shared DTOs, read services, and export seams
- connect external brokerage account state to fund-account review, cash movement, and reconciliation workflows through shared projections
- keep the Fund Accounts operator brief tied to shared account, provider-routing, retained balance-history, and `FundStructureSharedDataAccessDto` evidence while broader account/entity casework remains open
- deepen governance workflows without creating separate reporting or accounting stacks
- enforce the governance architecture guard: Security Master remains the sole instrument source, and governance DTO/service additions with instrument metadata must carry Security Master identity/provenance references


#### Wave 4 objective pass/fail gate (cockpit-style)

"Wave 4 objective" is **Pass** only when every governance/fund-ops criterion below is green in CI and locally reproducible. It is **Fail** if any criterion is red.

| Criterion | Required endpoint(s) + response fields | Required workstation surface behavior | Fail condition |
| --- | --- | --- | --- |
| Security Master conflict lifecycle is traceable end-to-end | `/api/security-master/conflicts`, `/api/security-master/conflicts/{conflictId}`, and `/api/security-master/conflicts/{conflictId}/resolve` must expose `ConflictReasonCode`, source-provenance identifiers, and resolution payload rationale (`ResolutionDecision`, `ResolutionRationale`, `Actor`, `TimestampUtc`, `CorrelationId`). | Operator can **search -> drill-in -> history -> resolution** for one conflicted instrument and see conflict reasons, source provenance, prior resolution history, and final resolution decision in one continuous flow. | Any missing linkage between conflict list/detail/resolution views, missing conflict reason code, missing source provenance, or missing explicit resolution rationale/audit chain in the same scenario run. |
| Corporate action provenance and parameter versioning remain explainable | `/api/security-master/corporate-actions` and `/api/security-master/trading-parameters` must return event provenance (`CorporateActionSource`, `IngestedAtUtc`) plus effective version fields (`EffectiveVersion`, `EffectiveFromUtc`, `SupersedesVersion`). | Operator can **search -> drill-in -> history -> resolution** from instrument view into corporate-action timeline and trading-parameter history, then resolve a flagged discrepancy with the effective-version trail visible. | Corporate-action timeline lacks provenance, trading-parameter change lacks effective-version traceability, or discrepancy resolution is recorded without explainable source/version linkage. |
| Governance audit trail is complete across fund-ops decisions | Governance workflow endpoints (`/api/fund-structure/workspace-view`, `/api/fund-structure/report-pack-preview`, and reconciliation decision endpoints) must emit audit metadata (`AuditActor`, `AuditTimestampUtc`, `CorrelationId`) and decision rationale fields for approvals/rejections. | Operator can **search -> drill-in -> history -> resolution** for an account/entity decision, inspect prior decision history, and complete or reject resolution with rationale that remains visible in history and governed output previews. | Any governance decision path that omits actor/timestamp/correlation, fails to retain decision rationale, or breaks history-to-resolution linkage between workspace and governed-output views. |

**Exit signal:** Governance becomes a real operator workflow with concrete review, drill-in, and governed-output seams built on the same contracts already used elsewhere in the workstation.

### Wave 5: Backtest Studio unification

**Why now:** Research becomes much stronger once Waves 1-4 have made the shared run model stable enough to unify native and Lean experiences cleanly.

**Focus:**

- unify native and Lean results under one result model
- improve comparison and run-diff tooling beyond the current web Research first cut, which can call shared compare/diff APIs for a selected run pair but does not yet unify native and Lean execution workflows
- broaden fill-model realism
- improve performance for larger windows where it materially changes operator experience

**Exit signal:** Backtesting feels like one coherent workflow regardless of engine.

### Wave 6: Live integration readiness

**Why now:** Live-adjacent credibility should follow, not precede, a finished paper workflow and validated provider trust.

**Focus:**

- validate at least one broader brokerage path against a real vendor surface
- add execution audit trail and human approval controls
- define safe `Paper -> Live` promotion gates
- formalize operator controls such as manual overrides, circuit breakers, and intervention flows

**Exit signal:** Meridian can support a controlled live-readiness story without overclaiming broad live-trading completion.

<a id="phase-16-assembly-level-performance-optimizations"></a>

### Optional advanced research / scale tracks

**Focus:**

- QuantScript deeper integration beyond the delivered local Run History and Research handoff presentation
- L3 inference and queue-aware execution simulation
- multi-instance coordination
- Phase 16 performance work
- broader advanced research extensions after the core workstation product is trustworthy and coherent

**Exit signal:** These deepen Meridian's ceiling after the core workstation product is operator-ready.

---

<a id="desktop-improvements"></a>
<a id="phase-8-repository-organization--optimization"></a>

## Wave DK Program (Focused Migration Wrapper for Waves 2-4)

To avoid piecemeal adoption, Meridian now treats the active workstation migration as a two-wave **Delivery Kernel (DK)** program that wraps and strengthens Waves 2-4 rather than running in parallel.

### Program intent

- keep one dependency-ordered path from provider trust to operator-ready cockpit, then into shared-model continuity and governance productization
- require the same quality gates in each wave: **parity pass**, **explainability pass**, **calibration pass**, and **operator sign-off**
- enforce shared interop contracts through one cross-wave owner so subsystem delivery does not drift into incompatible seams

### Wave DK1 - Data quality and provider trust hardening

**Scope alignment:** operationally reinforces Wave 2 and the trust-dependent portions of Wave 3.

**Primary outcomes:**

- maintain and extend the closed Wave 1 evidence gate into daily operator workflows
- make provider behavior, replay outcomes, and cockpit data surfaces explainable to operators
- calibrate trust metrics and promotion thresholds before expanding promotion scope

**Entry criteria (must all be true):**

1. **Parity entry:** Wave 1 matrix remains repo-closed for Alpaca, Robinhood (bounded), Yahoo, checkpoint reliability, and Parquet L2 proof.
2. **Explainability entry:** provider-confidence evidence is visible in operator-facing docs and workstation drill-ins, not only in scripts.
3. **Calibration entry:** baseline trust thresholds are declared for freshness, completeness, and replay consistency.
4. **Operator entry:** Data Operations and Trading operator reps agree on the DK1 pilot symbol/account set.

**Exit criteria (must all be true):**

1. **Parity pass:** paper-cockpit data views match validated provider and replay outputs for the agreed pilot set.
2. **Explainability pass:** every trust alert in scope has attributable source, reason code, and operator action guidance.
3. **Calibration pass:** trust thresholds are tuned against replay + paper session evidence with documented false-positive and false-negative review.
4. **Operator sign-off:** named Data Operations and Trading owners approve DK1 completion and unblock DK2 promotion scope.

### Wave DK2 - Promotion, export, and reconciliation continuity

**Scope alignment:** delivers the integration-critical path of Waves 3-4 (promotion workflow, export reliability, and governance reconciliation).

**Primary outcomes:**

- make `Backtest -> Paper -> Governance` promotion a single audited path
- ensure exports and governed outputs are consistent with shared run/portfolio/ledger contracts
- establish reconciliation as an always-on control rather than end-of-process cleanup

**Entry criteria (must all be true):**

1. **Parity entry:** DK1 exit is signed and shared run/portfolio/ledger DTO seams are the active path for pilot workflows.
2. **Explainability entry:** promotion and export decisions emit audit-grade rationale with linked run, portfolio, and ledger context.
3. **Calibration entry:** reconciliation tolerance bands and exception severities are defined per subsystem.
4. **Operator entry:** Governance and Trading operators accept the DK2 pilot operating playbook.

**Exit criteria (must all be true):**

1. **Parity pass:** promoted runs, exported artifacts, and reconciliation outputs agree across workstation, API, and governance views for pilot scenarios.
2. **Explainability pass:** operators can trace each promoted run to source data trust signals, approval chain, exported package, and reconciliation state.
3. **Calibration pass:** reconciliation thresholds and promotion controls are tuned with documented exception burn-down and zero unresolved critical mismatches.
4. **Operator sign-off:** Trading and Governance owners sign production-readiness for the DK2 scope.

### Subsystem ownership and interop governance

| Subsystem | Primary owner | Responsibilities |
| --- | --- | --- |
| Data quality + provider trust | Data Operations & Provider Reliability owner | Provider evidence gate maintenance, trust metrics, provider incident review |
| Promotion + paper-trading cockpit | Trading Workstation owner | Promotion controls, paper workflow reliability, operator controls |
| Export + packaging | Data Operations Export owner | Export contract parity, package lineage, operator-facing export diagnostics |
| Reconciliation + governance | Governance/Fund Ops owner | Reconciliation policy, exception workflow, governed outputs |
| Shared run/portfolio/ledger contracts | Shared Platform Interop owner (Architecture + Contracts) | Cross-subsystem DTO/version governance, compatibility policy, contract change review |

**Interop contract governance rule:** no DK subsystem can ship a contract-breaking change without Shared Platform Interop owner approval and a documented compatibility/rollback note.

### Risk register and rollback plans by subsystem

| Subsystem | Key risk | Leading indicator | Rollback plan |
| --- | --- | --- | --- |
| Data quality + provider trust | trust drift between validation scripts and cockpit surfaces | rising unresolved trust alert delta between scripts and UI | freeze promotion expansion, pin to last verified provider matrix + replay baseline, rerun DK1 calibration |
| Promotion + paper cockpit | promotion path divergence across UI/API | mismatched promotion state or approval chain in audits | revert promotion workflow to last signed contract version, disable new promotion lanes behind feature flags |
| Export + packaging | exported artifact schema drift or lineage gaps | increase in export validation failures or missing lineage links | roll back exporter contract version, regenerate artifacts from last good run snapshots |
| Reconciliation + governance | tolerance miscalibration causing exception floods or misses | sustained spike in unresolved critical exceptions | restore prior tolerance profile, reprocess affected window, require manual governance approval for new promotions |
| Shared interop contracts | uncoordinated DTO/version change cascades | cross-workspace contract test failures | revert to previous shared contract package/API shape and block downstream deploy until compatibility suite passes |

### Kernel readiness dashboard (single status surface)

Use [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md) as the single hand-authored status dashboard for DK wave and subsystem readiness.

Dashboard requirements:

- one row per subsystem with current DK wave state, gate status, owner, and next milestone
- explicit tracking of parity/explainability/calibration/operator-sign-off per subsystem
- linked evidence and rollback status so release decisions are auditable

### Alignment guardrail with Waves 2-4

DK1 and DK2 are **execution wrappers** for existing Waves 2-4, not new parallel scope:

- Wave 2 cockpit hardening work is planned and reported through DK1
- Wave 3 shared-model continuity is split: trust-dependent scope in DK1, promotion/export/reconciliation continuity in DK2
- Wave 4 governance productization readiness gates are tracked through DK2 exit criteria

Any proposed work item that cannot map to Wave 2, 3, or 4 plus DK1/DK2 gates should be treated as optional or deferred work, not core operator-readiness path.

### Immediate implementation commitments (2026-04-20 to 2026-05-29)

To move from planning into execution, the DK program now carries date-bounded commitments tracked in the dashboard:

1. **2026-04-20 -> 2026-05-01:** preserve the signed DK1 operator review around the emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet`, generated `ready-for-operator-review` parity packet, packet-bound sign-off file, trust rationale mapping, and threshold review; rerun packet-bound sign-off if any evidence changes.
2. **2026-04-20 -> 2026-05-01:** keep the shared interop compatibility matrix and contract-review cadence active, with dashboard status tied to the cross-wave owner.
3. **2026-05-02 -> 2026-05-15:** lock promotion rationale fields and operator approval checklist coverage for DK1 -> DK2 handoff.
4. **2026-05-09 -> 2026-05-22:** freeze governed export schema/version contract and validate pilot scenarios.
5. **2026-05-16 -> 2026-05-29:** calibrate reconciliation tolerance profiles and exception routing for governance sign-off readiness.

The implementation source of truth remains [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md), which must be updated weekly.

---

## Risks and Dependencies

- **Provider trust is still the first dependency.** The narrow Wave 1 gate is now closed, but downstream workflow polish still depends on preserving that evidence boundary instead of reopening provider scope by prose drift.
- **Stronger tests are not the same as broad live-vendor proof.** Replay, contract, and pipeline evidence materially improve confidence but do not close every vendor-runtime gap by themselves.
- **Cockpit hardening should precede live-readiness claims.** Meridian now has meaningful trading surfaces, but operator trust still matters more than feature count.
- **The shared run model must remain the center of gravity.** If Research, Trading, Portfolio, Ledger, and Governance drift apart again, the workstation migration loses its product logic.
- **Security Master must remain the authoritative seam.** It should enrich portfolio, ledger, reconciliation, and reporting flows rather than being reimplemented inside parallel governance workflows.

- **Governance DTO/Service review search guidance:** in governance-related PRs, explicitly scan for new instrument-term fields (`Symbol`, `Cusip`, `Isin`, `Coupon`, `Maturity`, `Issuer`, `Venue`, `AssetClass`) that appear without Security Master identity/provenance references. Treat that as a review blocker unless the code is adapter-only with an explicit mapping step back to Security Master.
- **Governance should extend shared DTOs, not invent a new stack.** Cash-flow, reconciliation, and reporting should reuse the same read-model and export seams already in place.
- **WPF migration should avoid page-level re-fragmentation.** The right move is more orchestration and view-model or service extraction, not more page-local logic.
- **Documentation drift is now a real delivery risk.** The planning set is large enough that roadmap, status, blueprint, and short-horizon docs need deliberate synchronization.

---

## Release Gates

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

1. **Wave 1 gates:** the active provider gate for Alpaca, Robinhood, and Yahoo is documented in executable evidence, checkpoint reliability plus Parquet L2 flush behavior are closed in repo tests, and `run-wave1-provider-validation.ps1` reproduces the offline gate while generated provider-validation packets are attached as date-stamped run outputs.
2. **Wave 2 gates:** the workstation exposes a dependable paper-trading cockpit through the shared readiness contract, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

Until then, Meridian is best described as feature-rich, structurally strong, and actively being productized into its intended workstation and fund-operations end state.

---

## Reference Documents

- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md)
- [`FULL_IMPLEMENTATION_TODO_2026_03_20.md`](FULL_IMPLEMENTATION_TODO_2026_03_20.md)
- [`IMPROVEMENTS.md`](IMPROVEMENTS.md)
- [`EVALUATIONS_AND_AUDITS.md`](EVALUATIONS_AND_AUDITS.md)
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md)
- [`../plans/assembly-performance-roadmap.md`](../plans/assembly-performance-roadmap.md)
