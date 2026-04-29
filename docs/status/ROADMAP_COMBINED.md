# Meridian - Combined Roadmap, Opportunities, and Target State

**Last Updated:** 2026-04-29
**Status:** Combined stakeholder-facing roadmap refresh aligned to the canonical roadmap, signed DK1 pilot sample-set/parity-packet evidence, packet-bound sign-off validation, cockpit readiness projection, the active web dashboard lane, the web Research run-library support slice, retained WPF shell support evidence, route-aware account-scoped WPF shell queue consumption of operator-inbox and run review-packet work items with shell-context attention cues, neutral demo-data fixture semantics, stronger desktop workflow automation evidence, and hardened WPF screenshot/manual evidence capture

This document is the shortest complete entry point into Meridian's current roadmap. [`ROADMAP.md`](ROADMAP.md) remains the authoritative source for wave order, retained completion claims, and the definition of core operator-readiness.

Use this with:

- [`ROADMAP.md`](ROADMAP.md) for the canonical roadmap
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) for the short-horizon execution slice
- [`../plans/meridian-pilot-workflow.md`](../plans/meridian-pilot-workflow.md) for the golden-path pilot workflow and scope filter
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) for the prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) for the compact product narrative
- [`production-status.md`](production-status.md) for current readiness posture

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

Meridian already has strong platform foundations, meaningful workstation flows in the active web dashboard plus retained WPF/local API support surfaces, shared run / portfolio / ledger read services, a delivered Security Master baseline, a Wave 2 trading-readiness contract with canonical promotion approval-checklist state, stable operator work-item IDs, an initial shared operator-inbox endpoint for readiness, actionable warning/critical run review-packet items, and reconciliation work items, route-aware WPF shell queue-button consumption of the primary inbox item into concrete workbenches with active-account `fundAccountId` propagation and shell-context attention cues, explicit risk/control audit explainability, acceptance-gate/overall-readiness projection, signed DK1 trust-gate packet projection with packet-bound sign-off validation, seeded reconciliation exception-route/tolerance/sign-off metadata, and a WPF shell/navigation baseline organized around the four workspace model with focused coverage for high-traffic pages, Position Blotter selection review/action readiness, shell context strips, the Trading desk briefing hero with stale replay count detail, Trading Hours session briefings for live-risk/pre-market/after-hours/closed planning states, the OrderBook order-flow posture strip, the Research desk briefing hero, the Data Operations desk briefing hero, the Provider Health posture briefing, the System Health triage briefing, the Notification Center history-recovery action, the Activity Log triage strip with export/clear header actions, the Watchlist posture strip plus pinned-first card loading, Messaging Hub delivery posture with refresh recency, StrategyRuns filter-aware recovery/run-scope presentation, BatchBacktest stateful results empty guidance, QuantScript local execution-history presentation with Research handoffs for mirrored runs, Security Master runtime/search recovery, Fund Accounts account-queue/provider-routing/shared-data and balance-evidence briefing state, Storage archive posture plus preview scope/guidance, canonical `ResearchShell` page/deep-link routing, neutral demo-data fixture mode, UI-automation page-state markers, corrected isolated restore/build workflow behavior, local single-instance mutex plus launch-argument forwarding coverage, and a WPF screenshot refresh workflow that can run scheduled/push/manual catalog or manual captures, publish diagnostics, and commit screenshots once after the capture matrix. The web Research run library now adds browser-side support evidence for retained-run review, two-run compare/diff readiness, promotion-history loading, and command-error alerts; it does not close Backtest Studio unification. The wave order remains simple and consistent across the planning set:

1. **Wave 1:** provider confidence and checkpoint evidence _(repo-closed, keep synchronized)_
2. **Wave 2:** paper-trading cockpit hardening
3. **Wave 3:** shared run / portfolio / ledger continuity
4. **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline
5. **Wave 5:** Backtest Studio unification
6. **Wave 6:** live integration readiness
7. **Optional advanced research / scale tracks**

Waves 1-4 define the core operator-readiness path. A focused two-wave Delivery Kernel wrapper now governs that path: **DK1** (data-quality + provider trust hardening) and **DK2** (promotion + export + reconciliation continuity). As of 2026-04-27, DK1 has a concrete Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` review contract, a date-stamped `dk1-pilot-parity-packet.*` evidence set, and a packet-bound sign-off file signed by the Data Operations, Provider Reliability, and Trading owners. Validation reports all four pilot samples ready, all four evidence documents validated, trust-rationale/calibration contracts validated, packet binding valid, and `operatorSignoff.validForDk1Exit=true`. Future DK1 reviews still need fresh date-stamped packets and matching packet-bound sign-off files when evidence changes. Shared interop readiness is **Ready for cadence monitoring** after the baseline `artifacts/contract-review/2026-04-27/contract-review-packet.*` packet and Shared Platform Interop owner approval locked the weekly Wednesday review cadence. The promotion handoff lane is early in progress through cockpit audit-feedback hardening, export is early in progress through governed report-pack schema/version checks, and reconciliation/governance is now early in progress through a file-backed break queue with review, resolve/dismiss, audit-history routes, seeded exception-route/tolerance/sign-off metadata, and a calibration-summary endpoint that aggregates profile-level readiness and sign-off posture. Waves 5-6 deepen the product and widen later claims. Optional advanced research / scale tracks remain outside core readiness.

The golden-path pilot workflow now gives those waves one product filter: trusted data -> research run -> run comparison -> paper promotion -> paper session -> portfolio / ledger review -> reconciliation -> governed report pack. New work should strengthen that path, unblock a later stage, or explicitly reduce scope that distracts from it.

---

## Current State

Use the canonical wave table in [`PROGRAM_STATE.md`](PROGRAM_STATE.md) for all status labels and target dates.

This document keeps concise framing only; detailed readiness evidence remains in [`production-status.md`](production-status.md) and full sequencing remains in [`ROADMAP.md`](ROADMAP.md).

---

## Opportunities

### 1. Wave 2: Harden the paper-trading cockpit already in code

The paper-trading cockpit should move from "implemented" to "dependable." The current repo now has a shared trading-readiness contract for session, replay, controls, recent risk/control audit evidence, missing-field explainability warnings, DK1 trust-gate packet/sign-off posture, promotion checklist, brokerage-sync, acceptance-gate status, and stable work-item posture, with `PromotionApprovalChecklist` defining the required review items for paper and live promotions. The local replay-audit hardening slice also records consistency, compared fill/order/ledger counts, last-persisted timestamps, and primary mismatch reason for readiness reconstruction, and the readiness gate now flags replay coverage as stale when active-session fill/order/ledger counts diverge after verification. The WPF Trading shell has a desk briefing hero that projects current focus, readiness tone, and next handoff from the same active-run/workflow/readiness inputs, OrderBook now has order-flow posture for depth/tape/spread monitoring, Position Blotter now has grouped selection review/action-readiness support for flatten/upsize decisions, and the main shell now exposes the shared operator inbox as a queue button that resolves known route metadata into concrete workbenches such as `AccountPortfolio`, `FundReconciliation`, and `SecurityMaster` while passing the active account context as `fundAccountId` for account-scoped brokerage/readiness blockers; the next step is proving those contracts through operator scenarios rather than treating the endpoint or shell coverage as completion.

### 2. Wave 3: Make the shared run / portfolio / ledger model the center of gravity

Research, Trading, and Governance should keep converging on the same run-centered seam. The web Research run library now gives that convergence browser-side support by letting operators select exactly two retained runs, enable compare/diff commands only when a valid pair exists, call shared compare/diff APIs, load promotion history, and see command failures as alerts. The retained Research desk briefing hero also gives that convergence a concrete WPF support surface by routing selected runs into run-detail, portfolio, and paper-promotion review handoffs through shared workstation data, while StrategyRuns now exposes visible-versus-recorded run scope, reset-filters recovery when search or mode filters hide retained runs, and comparison guidance when visible run state cannot produce a valid compare pair. The shared run review packet now returns stable run-scoped operator work-item IDs with workspace route/page hints for promotion, Security Master, continuity, and brokerage blockers. QuantScript now adds a local Run History tab with empty-state guidance, captured evidence summaries, and run-browser/detail/compare handoffs for mirrored Strategy Runs; treat that as shared-model support, not as closure of Wave 3 continuity.

RunCashFlow now adds the same kind of support to retained cash-flow drill-ins by explaining selected-run, missing-run, no-event, and loaded evidence states instead of showing blank ladder/event grids. Keep that as Wave 3 continuity evidence while broader governance cash-flow modeling remains open.

### 3. Wave 4: Productize governance and fund-operations on top of the delivered Security Master baseline

Security Master is already the delivered baseline, and Fund Accounts now has an initial stateful operator brief for fund context, account queues, provider route evidence, shared-data gaps, balance-evidence snapshot posture, and ready-for-reconciliation posture. Reconciliation breaks now carry seeded exception route, tolerance profile, tolerance band, required sign-off role, and sign-off status metadata, with `/api/workstation/reconciliation/calibration-summary` rolling those items into profile-level Ready/ReviewRequired/Blocked posture. The opportunity is turning those support surfaces into deeper account/entity, multi-ledger, cash-flow, calibrated reconciliation, and reporting workflows.

### 4. DK1 / Wave 1: Keep the closed provider-confidence and checkpoint-evidence gate synchronized

Wave 1 is now repo-closed, but it remains the trust boundary that every downstream readiness claim depends on. DK1 now turns that boundary into a signed pilot parity packet with an emitted `pilotReplaySampleSet`, generated date-stamped parity-packet artifacts, valid packet-bound owner sign-off, trust rationale mapping, and threshold calibration.

The Data Operations workspace now has concrete WPF support surfaces for this work: its desk briefing hero projects provider health, resumable backfills, storage health, collection sessions, export jobs, operational blockers, and next-handoff actions through `DataOperationsWorkspacePresentationBuilder`, while `ProviderHealth` adds a provider-posture briefing for stale snapshots, disconnected streaming sessions, mixed-provider states, and blocked backfill coverage. Treat those as support evidence for the Wave 1/DK1 operating lane, not as proof that provider trust or export readiness is closed by UI coverage alone.

### 5. Wave 5: Unify Backtest Studio after the core operator-readiness path is stable

Native and Lean backtesting should become one operator-facing workflow once the shared model is stable enough to support it cleanly.

### 6. Wave 6: Expand into controlled live integration readiness only after trust and paper-workflow gates are real

Live-readiness should follow proven provider evidence and a dependable paper workflow, not outrun them.

### 7. Optional advanced research / scale tracks

Remaining QuantScript expansion beyond the delivered local Run History and Research handoff presentation, queue-aware simulation, multi-instance coordination, and Phase 16 performance work deepen Meridian's ceiling after the core workstation product is already trustworthy and coherent.

Across Waves 1-4, keep WPF consolidation, shared DTOs, read models, workflow services, export seams, launch/deep-link automation, single-instance routing, fixture/demo-mode cues, and operator-grade validation supporting the active waves rather than becoming separate priorities. The WPF shell/navigation baseline, including the Trading, Research, and Data Operations desk briefing heroes, Trading Hours session briefing, OrderBook order-flow posture, the Provider Health posture briefing, System Health triage briefing, Notification Center filter recovery, Activity Log triage/export/clear support, Watchlist posture with pinned-first card ordering, StrategyRuns filter-aware recovery, BatchBacktest results empty guidance, QuantScript run-history handoffs, Security Master search recovery, Fund Accounts operator and balance-evidence briefing, shell-context operator queue attention, and the latest workflow-automation hardening, is now present enough to judge it by workflow value, not by additional shell surface area.

The latest validation-support update is the hardened `refresh-screenshots.yml` lane: selectable manual dispatch, least-privilege default permissions, per-workflow diagnostics, and a single post-matrix screenshot commit job. Treat it as release-evidence plumbing that improves screenshot/manual validation reliability; it does not change the Wave 2-4 acceptance gates.

The latest support-surface evidence also includes Welcome readiness progress, Storage archive growth/capacity/last-scan posture plus preview scope/guidance, OrderBook order-flow posture, compact shared deep-page command chrome with related-workflow/trust context, actionable shell-context attention detail, provider-degradation next actions that land on `ProviderHealth`, brokerage-sync queue routing into `AccountPortfolio`, bounded run review-packet inbox items for promotion/security/continuity blockers, and Trading hero handling that keeps warning or critical shared work items in an attention state before showing a ready desk. These improve orientation and handoff quality; they do not change the Wave 2-4 acceptance gates.

Delivery Kernel governance to avoid piecemeal adoption:

- **DK1 (maps to Wave 2 + trust-dependent Wave 3 scope):** requires parity, explainability, calibration, and operator sign-off before promotion scope expands; the pilot review contract is the emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` plus freshly generated `dk1-pilot-parity-packet.*` artifacts attached for the review run
- **DK2 (maps to Wave 3-4 integration scope):** requires the same four gates across promotion, export, and reconciliation
- **Subsystem ownership:** Data Operations, Trading, Export, Governance, and a Shared Platform Interop owner for contract governance
- **Single status surface:** track subsystem readiness and rollback posture in [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md)
- **Execution now active:** date-bounded implementation commitments run from 2026-04-20 through 2026-05-29 and are updated weekly in the dashboard

---

## Target End Product

Meridian's target end state is a self-hosted trading workstation and fund-operations platform organized around the visible `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` workspaces.

`Data` establishes evidence-backed provider trust, `Strategy` turns that data into reviewed runs and comparisons, `Trading` promotes approved runs into paper workflows, `Portfolio` and `Accounting` review the resulting account, portfolio, ledger, cash-flow, and reconciliation evidence, and `Reporting` turns the governed evidence set into retained report packs.

The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context.

---

## Core Operator-Readiness

Meridian can reasonably claim **core operator-readiness** when Wave gates and DK wrappers are both satisfied:

1. **Wave 1 gates:** the active gate for Alpaca, Robinhood, and Yahoo is documented in executable suites plus generated runtime attachments for bounded scenarios, checkpoint reliability plus Parquet L2 flush behavior are closed in repo tests, and `run-wave1-provider-validation.ps1` reproduces the offline gate.
2. **Wave 2 gates:** the workstation exposes a dependable paper-trading cockpit through the shared readiness contract, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

---

## Risks and Dependencies

- provider confidence remains the first dependency even though the narrow Wave 1 gate is now closed
- stronger replay, contract, and pipeline tests should raise confidence without being described as broad live-runtime closure
- cockpit hardening should happen before live-readiness claims
- the shared run model and delivered Security Master baseline must remain central as the product grows
- governance should extend shared contracts instead of creating a parallel subsystem
- documentation must stay synchronized as roadmap, workstation, and governance work continue to evolve
