# Meridian - Combined Roadmap, Opportunities, and Target State

**Last Updated:** 2026-04-20
**Status:** Combined stakeholder-facing roadmap refresh aligned to the canonical April 17 roadmap

This document is the shortest complete entry point into Meridian's current roadmap. [`ROADMAP.md`](ROADMAP.md) remains the authoritative source for wave order, retained completion claims, and the definition of core operator-readiness.

Use this with:

- [`ROADMAP.md`](ROADMAP.md) for the canonical roadmap
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) for the short-horizon execution slice
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) for the prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) for the compact product narrative
- [`production-status.md`](production-status.md) for current readiness posture

---

## Summary

Meridian already has strong platform foundations, meaningful workstation flows in web and WPF, shared run / portfolio / ledger read services, and a delivered Security Master baseline. The wave order remains simple and consistent across the planning set:

1. **Wave 1:** provider confidence and checkpoint evidence *(repo-closed, keep synchronized)*
2. **Wave 2:** paper-trading cockpit hardening
3. **Wave 3:** shared run / portfolio / ledger continuity
4. **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline
5. **Wave 5:** Backtest Studio unification
6. **Wave 6:** live integration readiness
7. **Optional advanced research / scale tracks**

Waves 1-4 define the core operator-readiness path. A focused two-wave Delivery Kernel wrapper now governs that path: **DK1** (data-quality + provider trust hardening) and **DK2** (promotion + export + reconciliation continuity). Waves 5-6 deepen the product and widen later claims. Optional advanced research / scale tracks remain outside core readiness.

---

## Current State

### Complete

- core ingestion, storage, replay, export, and data-quality foundations are materially strong
- the web and WPF workstation baselines already organize around `Research`, `Trading`, `Data Operations`, and `Governance`
- shared `StrategyRun`, portfolio, and ledger read services already feed workstation surfaces
- promotion endpoints and dashboard promotion surfaces are already in code
- Security Master is already the authoritative instrument-definition baseline across workstation and governance surfaces
- governance-facing reconciliation, direct-lending, and export/report-adjacent seams are already present in the repo

### Partial

- the active Wave 1 trust gate is closed for Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet L2 flush behavior, but that closure still needs deliberate doc, script, and artifact synchronization
- the paper-trading cockpit is real, but not yet hardened as a dependable daily-use lane
- shared run continuity is present, but not yet equally deep across every workspace and mode
- governance workflows now build on a delivered Security Master baseline, but multi-ledger, cash-flow, reconciliation, and reporting still need deeper productization
- WPF workflow-first consolidation and MVVM extraction remain active in high-traffic areas

---

## Opportunities

### 1. Wave 2: Harden the paper-trading cockpit already in code

The paper-trading cockpit should move from "implemented" to "dependable."

### 2. Wave 3: Make the shared run / portfolio / ledger model the center of gravity

Research, Trading, and Governance should keep converging on the same run-centered seam.

### 3. Wave 4: Productize governance and fund-operations on top of the delivered Security Master baseline

Security Master is already the delivered baseline; the opportunity is turning that baseline into deeper account/entity, multi-ledger, cash-flow, reconciliation, and reporting workflows.

### 4. Wave 1: Keep the closed provider-confidence and checkpoint-evidence gate synchronized

Wave 1 is now repo-closed, but it remains the trust boundary that every downstream readiness claim depends on.

### 5. Wave 5: Unify Backtest Studio after the core operator-readiness path is stable

Native and Lean backtesting should become one operator-facing workflow once the shared model is stable enough to support it cleanly.

### 6. Wave 6: Expand into controlled live integration readiness only after trust and paper-workflow gates are real

Live-readiness should follow proven provider evidence and a dependable paper workflow, not outrun them.

### 7. Optional advanced research / scale tracks

QuantScript expansion, queue-aware simulation, multi-instance coordination, and Phase 16 performance work deepen Meridian's ceiling after the core workstation product is already trustworthy and coherent.

Across Waves 1-4, keep WPF consolidation, shared DTOs, read models, workflow services, export seams, and operator-grade validation supporting the active waves rather than becoming separate priorities.

Delivery Kernel governance to avoid piecemeal adoption:

- **DK1 (maps to Wave 2 + trust-dependent Wave 3 scope):** requires parity, explainability, calibration, and operator sign-off before promotion scope expands
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
