# Meridian - Combined Roadmap, Opportunities, and Target State

**Last Updated:** 2026-04-08
**Status:** Combined stakeholder-facing roadmap refresh aligned to the canonical April 8 roadmap

This document is the shortest complete entry point into Meridian's current roadmap. [`ROADMAP.md`](ROADMAP.md) remains the authoritative source for wave order, retained completion claims, and the definition of core operator-readiness.

Use this with:

- [`ROADMAP.md`](ROADMAP.md) for the canonical roadmap
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) for the short-horizon execution slice
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) for the prioritized opportunity framing
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) for the compact product narrative
- [`production-status.md`](production-status.md) for current readiness posture

---

## Summary

Meridian already has strong platform foundations, meaningful workstation flows in web and WPF, shared run / portfolio / ledger read services, and a delivered Security Master baseline. The remaining order is now simple and consistent across the planning set:

1. **Wave 1:** provider confidence and checkpoint evidence
2. **Wave 2:** paper-trading cockpit hardening
3. **Wave 3:** shared run / portfolio / ledger continuity
4. **Wave 4:** governance and fund-operations productization on top of the delivered Security Master baseline
5. **Wave 5:** Backtest Studio unification
6. **Wave 6:** live integration readiness
7. **Optional advanced research / scale tracks**

Waves 1-4 define the core operator-readiness path. Waves 5-6 deepen the product and widen later claims. Optional advanced research / scale tracks remain outside that core path.

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

- provider trust is still uneven across key providers and checkpoint scenarios
- the paper-trading cockpit is real, but not yet hardened as a dependable daily-use lane
- shared run continuity is present, but not yet equally deep across every workspace and mode
- governance workflows now build on a delivered Security Master baseline, but multi-ledger, cash-flow, reconciliation, and reporting still need deeper productization
- WPF workflow-first consolidation and MVVM extraction remain active in high-traffic areas

---

## Opportunities

### 1. Wave 1: Close provider-confidence and checkpoint-evidence gaps first

Provider evidence and backfill checkpoint confidence still gate every downstream readiness claim.

### 2. Wave 2: Harden the paper-trading cockpit already in code

The paper-trading cockpit should move from "implemented" to "dependable."

### 3. Wave 3: Make the shared run / portfolio / ledger model the center of gravity

Research, Trading, and Governance should keep converging on the same run-centered seam.

### 4. Wave 4: Productize governance and fund-operations on top of the delivered Security Master baseline

Security Master is already the delivered baseline; the opportunity is turning that baseline into deeper account/entity, multi-ledger, cash-flow, reconciliation, and reporting workflows.

### 5. Wave 5: Unify Backtest Studio after the core operator-readiness path is stable

Native and Lean backtesting should become one operator-facing workflow once the shared model is stable enough to support it cleanly.

### 6. Wave 6: Expand into controlled live integration readiness only after trust and paper-workflow gates are real

Live-readiness should follow proven provider evidence and a dependable paper workflow, not outrun them.

### 7. Optional advanced research / scale tracks

QuantScript expansion, queue-aware simulation, multi-instance coordination, and Phase 16 performance work deepen Meridian's ceiling after the core workstation product is already trustworthy and coherent.

Across Waves 1-4, keep WPF consolidation, shared DTOs, read models, workflow services, export seams, and operator-grade validation supporting the active waves rather than becoming separate priorities.

---

## Target End Product

Meridian's target end state is a self-hosted trading workstation and fund-operations platform with four connected workspaces: `Research`, `Trading`, `Data Operations`, and `Governance`.

Data Operations establishes evidence-backed provider trust, Research turns that data into reviewed runs, Trading promotes approved runs into paper workflows, and Governance operates on the same instruments and records through the delivered Security Master baseline, portfolio, ledger, reconciliation, cash-flow, and reporting workflows.

The product promise is continuity: one operator can move from data trust to research, paper trading, portfolio and ledger review, and governance workflows without leaving Meridian or losing audit context.

---

## Core Operator-Readiness

Meridian can reasonably claim **core operator-readiness** when the wave-aligned gates below are true:

1. **Wave 1 gates:** major providers have documented replay or runtime validation evidence, and backfill checkpoints plus gap handling are validated across representative providers and date ranges.
2. **Wave 2 gates:** the web workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3 gates:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4 gates:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts rather than blueprint-only intent.

Waves 5 and 6 deepen the product and widen later claims, but they are not prerequisites for core operator-readiness.

---

## Risks and Dependencies

- provider confidence remains the first dependency
- stronger replay, contract, and pipeline tests should raise confidence without being described as broad live-runtime closure
- cockpit hardening should happen before live-readiness claims
- the shared run model and delivered Security Master baseline must remain central as the product grows
- governance should extend shared contracts instead of creating a parallel subsystem
- documentation must stay synchronized as roadmap, workstation, and governance work continue to evolve
