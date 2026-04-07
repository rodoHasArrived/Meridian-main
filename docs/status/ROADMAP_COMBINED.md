# Meridian - Combined Roadmap, Opportunities, and Target State

**Last Updated:** 2026-04-06
**Status:** Combined stakeholder-facing roadmap refresh

This document is the shortest complete entry point into Meridian's current roadmap. It combines the active roadmap refresh, the highest-value opportunities, and the target end-state product story into one repo-grounded snapshot. `ROADMAP.md` remains the authoritative wave-structured roadmap.

Use this with:

- [`ROADMAP.md`](ROADMAP.md) for the full wave-structured roadmap
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md) for the time-boxed plan
- [`OPPORTUNITY_SCAN.md`](OPPORTUNITY_SCAN.md) for the prioritized opportunity list
- [`TARGET_END_PRODUCT.md`](TARGET_END_PRODUCT.md) for the concise product narrative

---

## Summary

Meridian is no longer mainly blocked on missing platform foundations. The repo already has a strong ingestion, storage, replay, backtesting, execution, and workstation baseline, plus visible governance seams. April 5-6 backlog and evidence updates strengthen that picture, but they do not change the delivery order: the roadmap priority is still to close trust gaps, finish workflow continuity, and turn the product surfaces already in code into a genuinely operator-ready trading workstation and fund-operations platform.

---

## Current State

### What Is Complete

- Core ingestion, storage, replay, export, and data-quality foundations are materially strong.
- Shared workstation endpoints and a React workstation shell now cover Research, Trading, Data Operations, and Governance.
- Research, Trading, Data Operations, and Governance each have real workflows in code, not only route placeholders.
- Shared run, portfolio, and ledger read services exist and already feed workstation surfaces.
- WPF shell modernization is complete and run-centered workstation pages are active in the desktop app.
- Recent supporting momentum includes the new desktop Quickstart panel, standalone WPF export workflow, and cleared pre-existing test failures.
- Security Master, reconciliation, direct lending, and governance-facing endpoints are already present in the repo.

### What Is Partial

- Provider trust is still uneven across key providers even after stronger IB, NYSE, and StockSharp test evidence landed on April 6.
- Backfill reliability needs stronger evidence across representative windows.
- The web cockpit is real, but not yet fully hardened as a daily operator surface.
- Shared run continuity is present, but not yet equally deep across every workspace and mode.
- Security Master and governance workflows are visible, but not yet fully productized as the main cross-workspace governance seam.
- WPF still needs deeper workflow-first page work and MVVM extraction on high-traffic surfaces.

---

## Opportunities

### 1. Close trust gaps first

Provider evidence and backfill checkpoint confidence still gate every downstream readiness claim.

### 2. Harden the cockpit already in code

The paper-trading cockpit should now move from "implemented" to "dependable."

### 3. Make the shared run model the center of gravity

Research, trading, portfolio, ledger, and governance should all keep converging on the same run-centered seam.

### 4. Productize Security Master and governance

Security Master, multi-ledger, reconciliation, cash-flow, and reporting are now differentiated product opportunities on the main delivery path, not speculative ideas or a separate future track.

### 5. Keep architecture simple while the UX expands

Shared DTOs, read models, orchestration services, and export seams should stay the default integration boundary.

---

## Target End Product

The finished Meridian product is a self-hosted trading workstation and fund-operations platform with four connected workspaces:

- **Research** for experiments, comparisons, and promotion review
- **Trading** for positions, orders, fills, replay, and risk-managed paper workflows
- **Data Operations** for provider trust, backfills, quality, storage, and exports
- **Governance** for Security Master, portfolio, ledger, reconciliation, cash-flow, and reporting

The key product promise is continuity: trusted data feeds research, research promotes into trading, trading produces portfolio and ledger state, and governance operates on those same artifacts without leaving the platform.

---

## Recommended Next Waves

### Wave 1

Provider trust and backfill evidence closure.

### Wave 2

Paper-trading cockpit hardening and operator acceptance criteria.

### Wave 3

Shared run, portfolio, ledger, and reconciliation continuity across workspaces.

### Wave 4

Security Master and governance productization.

### Wave 5

Backtest Studio unification across native and Lean.

### Wave 6

Controlled live integration readiness after paper and trust gates are genuinely closed.

---

## Risks and Dependencies

- Provider trust remains the first dependency.
- Stronger replay, contract, and pipeline tests should raise confidence without being described as full live-runtime closure.
- Cockpit hardening should happen before live-readiness claims.
- Shared run and Security Master seams must remain central as the product grows.
- Governance should extend shared contracts instead of creating a parallel subsystem.
- Documentation must stay synchronized as roadmap, workstation, and governance work continue to evolve.
