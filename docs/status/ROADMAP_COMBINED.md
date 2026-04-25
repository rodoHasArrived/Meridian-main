# Meridian - Combined Roadmap, Opportunities, and Target State

**Last Updated:** 2026-04-25
**Status:** Combined stakeholder-facing roadmap refresh aligned to the canonical roadmap, DK1 pilot sample-set/parity-packet evidence, and current WPF shell baseline

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
| W2 | Trading Workstation | In Progress | 2026-05-29 | [`ROADMAP.md#wave-2-web-paper-trading-cockpit-completion`](ROADMAP.md#wave-2-web-paper-trading-cockpit-completion) |
| W3 | Shared Platform Interop | In Progress | 2026-06-26 | [`ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity`](ROADMAP.md#wave-3-shared-run--portfolio--ledger-continuity) |
| W4 | Governance + Fund Ops | In Progress | 2026-07-24 | [`ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline`](ROADMAP.md#wave-4-governance-and-fund-operations-productization-on-top-of-the-delivered-security-master-baseline) |
| W5 | Research Platform | Planned | 2026-08-21 | [`ROADMAP.md#wave-5-backtest-studio-unification`](ROADMAP.md#wave-5-backtest-studio-unification) |
| W6 | Execution + Brokerage Integrations | Planned | 2026-09-18 | [`ROADMAP.md#wave-6-live-integration-readiness`](ROADMAP.md#wave-6-live-integration-readiness) |
<!-- program-state:end -->

---

## Summary

Meridian already has strong platform foundations, meaningful workstation flows in web and WPF, shared run / portfolio / ledger read services, a delivered Security Master baseline, a Wave 2 trading-readiness contract with canonical promotion approval-checklist state, and a WPF shell/navigation baseline organized around the four workspace model with focused coverage for high-traffic pages and shell context strips. The wave order remains simple and consistent across the planning set:

1. **Wave 1:** provider confidence and checkpoint evidence _(repo-closed, keep synchronized)_
2. **Wave 2:** paper-trading cockpit hardening
3. **Wave 3:** shared run / portfolio / ledger continuity
4. **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline
5. **Wave 5:** Backtest Studio unification
6. **Wave 6:** live integration readiness
7. **Optional advanced research / scale tracks**

Waves 1-4 define the core operator-readiness path. A focused two-wave Delivery Kernel wrapper now governs that path: **DK1** (data-quality + provider trust hardening) and **DK2** (promotion + export + reconciliation continuity). As of 2026-04-25, DK1 has a concrete Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` review contract and a generated `dk1-pilot-parity-packet.*` artifact; the latest packet is `ready-for-operator-review` with validated evidence-document checks and no packet blockers. DK1 still needs operator sign-off plus workflow-facing explainability/calibration review, and shared interop readiness remains **At Risk / in progress**. The promotion handoff lane is early in progress through cockpit audit-feedback hardening, export is early in progress through governed report-pack schema/version checks, and reconciliation/governance is now early in progress through a file-backed break queue with review, resolve/dismiss, and audit-history routes. Waves 5-6 deepen the product and widen later claims. Optional advanced research / scale tracks remain outside core readiness.

---

## Current State

Use the canonical wave table in [`PROGRAM_STATE.md`](PROGRAM_STATE.md) for all status labels and target dates.

This document keeps concise framing only; detailed readiness evidence remains in [`production-status.md`](production-status.md) and full sequencing remains in [`ROADMAP.md`](ROADMAP.md).

---

## Opportunities

### 1. Wave 2: Harden the paper-trading cockpit already in code

The paper-trading cockpit should move from "implemented" to "dependable." The current repo now has a shared trading-readiness contract for session, replay, controls, promotion checklist, brokerage-sync, and work-item posture, with `PromotionApprovalChecklist` defining the required review items for paper and live promotions; the next step is proving that contract through operator scenarios rather than treating the endpoint as completion.

### 2. Wave 3: Make the shared run / portfolio / ledger model the center of gravity

Research, Trading, and Governance should keep converging on the same run-centered seam.

### 3. Wave 4: Productize governance and fund-operations on top of the delivered Security Master baseline

Security Master is already the delivered baseline; the opportunity is turning that baseline into deeper account/entity, multi-ledger, cash-flow, reconciliation, and reporting workflows.

### 4. DK1 / Wave 1: Keep the closed provider-confidence and checkpoint-evidence gate synchronized

Wave 1 is now repo-closed, but it remains the trust boundary that every downstream readiness claim depends on. DK1 now turns that boundary into a pilot parity packet with an emitted `pilotReplaySampleSet`, generated parity-packet artifacts, trust rationale mapping, threshold calibration, and pending operator sign-off.

### 5. Wave 5: Unify Backtest Studio after the core operator-readiness path is stable

Native and Lean backtesting should become one operator-facing workflow once the shared model is stable enough to support it cleanly.

### 6. Wave 6: Expand into controlled live integration readiness only after trust and paper-workflow gates are real

Live-readiness should follow proven provider evidence and a dependable paper workflow, not outrun them.

### 7. Optional advanced research / scale tracks

QuantScript expansion, queue-aware simulation, multi-instance coordination, and Phase 16 performance work deepen Meridian's ceiling after the core workstation product is already trustworthy and coherent.

Across Waves 1-4, keep WPF consolidation, shared DTOs, read models, workflow services, export seams, and operator-grade validation supporting the active waves rather than becoming separate priorities. The WPF shell/navigation baseline is now present enough to judge it by workflow value, not by additional shell surface area.

Delivery Kernel governance to avoid piecemeal adoption:

- **DK1 (maps to Wave 2 + trust-dependent Wave 3 scope):** requires parity, explainability, calibration, and operator sign-off before promotion scope expands; the pilot review contract is the emitted Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` plus generated `dk1-pilot-parity-packet.*` artifacts
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

1. **Wave 1 gates:** the active gate for Alpaca, Robinhood, and Yahoo is documented in executable suites or committed runtime artifacts, checkpoint reliability plus Parquet L2 flush behavior are closed in repo tests, and `run-wave1-provider-validation.ps1` reproduces the offline gate.
2. **Wave 2 gates:** the web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
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
