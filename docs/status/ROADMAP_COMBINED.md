# Meridian - Combined Roadmap, Opportunities, and Target State

**Last Updated:** 2026-04-27
**Status:** Combined stakeholder-facing roadmap refresh aligned to the canonical roadmap, DK1 pilot sample-set/parity-packet evidence, packet-bound sign-off preflight, cockpit readiness projection, route-aware WPF shell queue consumption of operator-inbox work items, and current WPF shell baseline including Research, Trading, Data Operations, Provider Health, System Health, Notification Center, Activity Log, Watchlist, Messaging Hub, StrategyRuns, QuantScript run history, Security Master, and Fund Accounts briefing/triage/recovery surfaces, canonical `ResearchShell` launch routing, single-instance launch-argument forwarding, neutral demo-data fixture semantics, and stronger desktop workflow automation evidence

This document is the shortest complete entry point into Meridian's current roadmap. [`ROADMAP.md`](ROADMAP.md) remains the authoritative source for wave order, retained completion claims, and the definition of core operator-readiness.

Use this with:

- [`ROADMAP.md`](ROADMAP.md) for the canonical roadmap
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) for the short-horizon execution slice
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) for the prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) for the compact product narrative
- [`production-status.md`](production-status.md) for current readiness posture

---

## Canonical Program State

Program wave status is canonical in [`PROGRAM_STATE.md`](PROGRAM_STATE.md). Any wave status wording in this file is explanatory context only.

<!-- program-state:begin -->
| Wave | Owner | Status | Target Date | Evidence Link |
| --- | --- | --- | --- | --- |
| W1 | Data Operations + Provider Reliability | Done | 2026-04-17 | [`production-status.md#provider-evidence-summary`](production-status.md#provider-evidence-summary) |
| W2 | Trading Workstation | In Progress | 2026-05-29 | [`ROADMAP.md#wave-2-workstation-paper-trading-cockpit-completion`](ROADMAP.md#wave-2-workstation-paper-trading-cockpit-completion) |
| W3 | Shared Platform Interop | In Progress | 2026-06-26 | [`ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity`](ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity) |
| W4 | Governance + Fund Ops | In Progress | 2026-07-24 | [`ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline`](ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline) |
| W5 | Research Platform | Planned | 2026-08-21 | [`ROADMAP.md#wave-5-backtest-studio-unification`](ROADMAP.md#wave-5-backtest-studio-unification) |
| W6 | Execution + Brokerage Integrations | Planned | 2026-09-18 | [`ROADMAP.md#wave-6-live-integration-readiness`](ROADMAP.md#wave-6-live-integration-readiness) |
<!-- program-state:end -->

---

## Summary

Meridian already has strong platform foundations, meaningful workstation flows in the primary WPF shell plus retained local API/web support surfaces, shared run / portfolio / ledger read services, a delivered Security Master baseline, a Wave 2 trading-readiness contract with canonical promotion approval-checklist state, stable operator work-item IDs, an initial shared operator-inbox endpoint for readiness and reconciliation work items, route-aware WPF shell queue-button consumption of the primary inbox item into concrete workbenches, explicit risk/control audit explainability, acceptance-gate/overall-readiness projection, DK1 trust-gate packet projection with packet-bound sign-off preflight, and a WPF shell/navigation baseline organized around the four workspace model with focused coverage for high-traffic pages, Position Blotter selection review/action readiness, shell context strips, the Trading desk briefing hero, the Research desk briefing hero, the Data Operations desk briefing hero, the Provider Health posture briefing, the System Health triage briefing, the Notification Center history-recovery action, the Activity Log triage strip, the Watchlist posture strip, Messaging Hub delivery posture with refresh recency, StrategyRuns filter-aware recovery/run-scope presentation, QuantScript local execution-history presentation with Research handoffs for mirrored runs, Security Master runtime/search recovery, Fund Accounts account-queue/provider-routing/shared-data briefing state, canonical `ResearchShell` page/deep-link routing, neutral demo-data fixture mode, UI-automation page-state markers, corrected isolated restore/build workflow behavior, and local single-instance mutex plus launch-argument forwarding coverage. The wave order remains simple and consistent across the planning set:

1. **Wave 1:** provider confidence and checkpoint evidence _(repo-closed, keep synchronized)_
2. **Wave 2:** paper-trading cockpit hardening
3. **Wave 3:** shared run / portfolio / ledger continuity
4. **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline
5. **Wave 5:** Backtest Studio unification
6. **Wave 6:** live integration readiness
7. **Optional advanced research / scale tracks**

Waves 1-4 define the core operator-readiness path. A focused two-wave Delivery Kernel wrapper now governs that path: **DK1** (data-quality + provider trust hardening) and **DK2** (promotion + export + reconciliation continuity). As of 2026-04-27, DK1 has a concrete Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` review contract and generated `dk1-pilot-parity-packet.*` artifacts, but those generated provider-validation packets are no longer retained in git. Operator review must therefore start from a fresh date-stamped packet under `artifacts/provider-validation/_automation/<yyyy-mm-dd>/` and its bound sign-off file. The sign-off helper can bind a sign-off template to that exact packet through `packetReview`, and validation rejects copied or stale sign-off files when the packet path, generated timestamp, status, or readiness contract no longer matches. The trading readiness lane projects that packet, pending sign-off state, sample-review evidence, evidence-document posture, and trust-rationale/baseline-threshold contract checks as operator-visible readiness, but DK1 still needs operator sign-off plus workflow-facing explainability/calibration review. Shared interop readiness is **Ready for cadence monitoring** after the baseline `artifacts/contract-review/2026-04-27/contract-review-packet.*` packet and Shared Platform Interop owner approval locked the weekly Wednesday review cadence. The promotion handoff lane is early in progress through cockpit audit-feedback hardening, export is early in progress through governed report-pack schema/version checks, and reconciliation/governance is now early in progress through a file-backed break queue with review, resolve/dismiss, and audit-history routes. Waves 5-6 deepen the product and widen later claims. Optional advanced research / scale tracks remain outside core readiness.

---

## Current State

Use the canonical wave table in [`PROGRAM_STATE.md`](PROGRAM_STATE.md) for all status labels and target dates.

This document keeps concise framing only; detailed readiness evidence remains in [`production-status.md`](production-status.md) and full sequencing remains in [`ROADMAP.md`](ROADMAP.md).

---

## Opportunities

### 1. Wave 2: Harden the paper-trading cockpit already in code

The paper-trading cockpit should move from "implemented" to "dependable." The current repo now has a shared trading-readiness contract for session, replay, controls, recent risk/control audit evidence, missing-field explainability warnings, DK1 trust-gate packet/sign-off posture, promotion checklist, brokerage-sync, acceptance-gate status, and stable work-item posture, with `PromotionApprovalChecklist` defining the required review items for paper and live promotions. The local replay-audit hardening slice also records consistency, compared fill/order/ledger counts, last-persisted timestamps, and primary mismatch reason for readiness reconstruction, and the readiness gate now flags replay coverage as stale when active-session fill/order/ledger counts diverge after verification. The WPF Trading shell has a desk briefing hero that projects current focus, readiness tone, and next handoff from the same active-run/workflow/readiness inputs, Position Blotter now has grouped selection review/action-readiness support for flatten/upsize decisions, and the main shell now exposes the shared operator inbox as a queue button that resolves known route metadata into concrete workbenches such as `TradingShell`, `FundReconciliation`, and `SecurityMaster`; the next step is proving those contracts through operator scenarios rather than treating the endpoint or shell coverage as completion.

### 2. Wave 3: Make the shared run / portfolio / ledger model the center of gravity

Research, Trading, and Governance should keep converging on the same run-centered seam. The Research desk briefing hero now gives that convergence a concrete WPF support surface by routing selected runs into run-detail, portfolio, and paper-promotion review handoffs through shared workstation data, while StrategyRuns now exposes visible-versus-recorded run scope and reset-filters recovery when search or mode filters hide retained runs. The shared run review packet now returns stable run-scoped operator work-item IDs with workspace route/page hints for promotion, Security Master, continuity, and brokerage blockers. QuantScript now adds a local Run History tab with empty-state guidance, captured evidence summaries, and run-browser/detail/compare handoffs for mirrored Strategy Runs; treat that as shared-model support, not as closure of Wave 3 continuity.

### 3. Wave 4: Productize governance and fund-operations on top of the delivered Security Master baseline

Security Master is already the delivered baseline, and Fund Accounts now has an initial stateful operator brief for fund context, account queues, provider route evidence, shared-data gaps, and ready-for-reconciliation posture. The opportunity is turning those support surfaces into deeper account/entity, multi-ledger, cash-flow, reconciliation, and reporting workflows.

### 4. DK1 / Wave 1: Keep the closed provider-confidence and checkpoint-evidence gate synchronized

Wave 1 is now repo-closed, but it remains the trust boundary that every downstream readiness claim depends on. DK1 now turns that boundary into a pilot parity packet with an emitted `pilotReplaySampleSet`, generated date-stamped parity-packet artifacts, packet-bound sign-off preflight, trust rationale mapping, threshold calibration, and pending operator sign-off.

The Data Operations workspace now has concrete WPF support surfaces for this work: its desk briefing hero projects provider health, resumable backfills, storage health, collection sessions, export jobs, operational blockers, and next-handoff actions through `DataOperationsWorkspacePresentationBuilder`, while `ProviderHealth` adds a provider-posture briefing for stale snapshots, disconnected streaming sessions, mixed-provider states, and blocked backfill coverage. Treat those as support evidence for the Wave 1/DK1 operating lane, not as proof that provider trust or export readiness is closed by UI coverage alone.

### 5. Wave 5: Unify Backtest Studio after the core operator-readiness path is stable

Native and Lean backtesting should become one operator-facing workflow once the shared model is stable enough to support it cleanly.

### 6. Wave 6: Expand into controlled live integration readiness only after trust and paper-workflow gates are real

Live-readiness should follow proven provider evidence and a dependable paper workflow, not outrun them.

### 7. Optional advanced research / scale tracks

Remaining QuantScript expansion beyond the delivered local Run History and Research handoff presentation, queue-aware simulation, multi-instance coordination, and Phase 16 performance work deepen Meridian's ceiling after the core workstation product is already trustworthy and coherent.

Across Waves 1-4, keep WPF consolidation, shared DTOs, read models, workflow services, export seams, launch/deep-link automation, single-instance routing, fixture/demo-mode cues, and operator-grade validation supporting the active waves rather than becoming separate priorities. The WPF shell/navigation baseline, including the Trading, Research, and Data Operations desk briefing heroes, the Provider Health posture briefing, System Health triage briefing, Notification Center filter recovery, Activity Log triage, Watchlist posture, StrategyRuns filter-aware recovery, QuantScript run-history handoffs, Security Master search recovery, Fund Accounts operator briefing, and the latest workflow-automation hardening, is now present enough to judge it by workflow value, not by additional shell surface area.

Delivery Kernel governance to avoid piecemeal adoption:

- **DK1 (maps to Wave 2 + trust-dependent Wave 3 scope):** requires parity, explainability, calibration, and operator sign-off before promotion scope expands; the pilot review contract is the emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` plus freshly generated `dk1-pilot-parity-packet.*` artifacts attached for the review run
- **DK2 (maps to Wave 3-4 integration scope):** requires the same four gates across promotion, export, and reconciliation
- **Subsystem ownership:** Data Operations, Trading, Export, Governance, and a Shared Platform Interop owner for contract governance
- **Single status surface:** track subsystem readiness and rollback posture in [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md)
- **Execution now active:** date-bounded implementation commitments run from 2026-04-20 through 2026-05-29 and are updated weekly in the dashboard

---

## Target End Product

Meridian's target end state is a self-hosted trading workstation and fund-operations platform with four connected workspaces: `Research`, `Trading`, `Data Operations`, and `Governance`.

Data Operations establishes evidence-backed provider trust, Research turns that data into reviewed runs, Trading promotes approved runs into paper workflows, and Governance operates on the same instruments and records through the delivered Security Master baseline, portfolio, ledger, reconciliation, cash-flow, and reporting workflows.

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
