# Meridian - Opportunity Scan

**Last Updated:** 2026-04-07
**Status:** Repo-grounded opportunity scan aligned to the current roadmap refresh

This document turns the current repository state into prioritized opportunities. It is meant to be additive to [`ROADMAP.md`](ROADMAP.md), not a duplicate backlog.

---

## Snapshot

Meridian has crossed an important threshold: the repo now contains meaningful workstation flows in web and WPF, shared run and ledger read models, a delivered Security Master seam, reconciliation seams, and a strong data and storage baseline. The April 6-7 roadmap, backlog, inventory, and production-status updates reinforce that this is a closure problem, not a missing-foundation problem. The best opportunities now are the ones that remove ambiguity, close trust gaps, and turn already-implemented seams into finished operator workflows.

---

## Top Opportunities

### 1. Close the provider-trust gap

- **Category:** provider readiness
- **Gap:** Provider breadth is strong, but replay/runtime proof is still uneven across Polygon, Interactive Brokers, StockSharp, and NYSE.
- **User or operator value:** Operators can trust research, replay, and promotion decisions only when provider claims are backed by concrete evidence.
- **Dependency it unlocks:** Clean readiness gates for paper trading, promotion, and future live validation.
- **Placement:** Critical path.

### 2. Harden the paper-trading cockpit into a daily-use lane

- **Category:** workflow completion
- **Gap:** The web trading cockpit now has real surfaces for positions, orders, fills, replay, sessions, and promotion, but it still needs tighter acceptance criteria and stronger hardening.
- **User or operator value:** This turns a featureful cockpit into a dependable operating surface rather than an impressive demo path.
- **Dependency it unlocks:** Credible `Backtest -> Paper` workflow continuity and later live-readiness work.
- **Placement:** Critical path.

### 3. Make the shared run model the real backbone of the product

- **Category:** workflow completion
- **Gap:** Shared run, portfolio, and ledger services exist, but not every workspace uses them with equal depth yet.
- **User or operator value:** One run-centered model makes research, trading, portfolio, ledger, and governance behavior easier to follow and trust.
- **Dependency it unlocks:** Cleaner cross-workspace UX and less duplicated orchestration logic.
- **Placement:** Critical path.

### 4. Build governance on top of the delivered Security Master seam

- **Category:** flagship product capabilities
- **Gap:** Security Master is now the authoritative instrument-definition seam, but the workflows built on top of it still need account/entity, multi-ledger, cash-flow, reconciliation, and reporting depth.
- **User or operator value:** Instrument identity, classification, and economic-definition trust can now be turned into broader governance and fund-operations reliability.
- **Dependency it unlocks:** Better reconciliation quality, cleaner reporting, and a credible front-, middle-, and back-office story.
- **Placement:** Critical-path continuation after trust and shared-model closure.

### 5. Keep workstation delivery architecture-simple

- **Category:** architecture simplification
- **Gap:** The product surface is broad enough that page-local orchestration could easily creep back in, especially in WPF migration work.
- **User or operator value:** Cleaner seams reduce regressions and make the product easier to evolve across both web and desktop.
- **Dependency it unlocks:** Faster delivery across later workstation and governance waves.
- **Placement:** Continuous supporting track.

### 6. Strengthen operator-facing testing and validation

- **Category:** testing and validation
- **Gap:** Workstation flows have grown faster than the acceptance story around cross-workspace behavior, replay confidence, and operator-grade regression coverage.
- **User or operator value:** Better validation reduces the chance that Meridian looks connected but behaves inconsistently under real use.
- **Dependency it unlocks:** Safer roadmap execution and stronger pilot credibility.
- **Placement:** Critical-path support track.

### 7. Preserve WPF momentum without letting it become parallel UX debt

- **Category:** operator UX
- **Gap:** WPF shell modernization is complete, but the high-traffic pages still need workflow-first redesign and more MVVM extraction.
- **User or operator value:** Desktop users get clearer, more maintainable flows instead of a polished shell wrapped around page-fragmented behavior.
- **Dependency it unlocks:** Better parity between the workstation vision and the active desktop experience.
- **Placement:** Near-term supporting wave.

---

## Optional Tracks

These are real opportunities, but they should not outrank the items above:

- deeper QuantScript workflow integration
- L3 inference and queue-aware execution simulation
- multi-instance coordination
- Phase 16 performance optimization
- Phase 1.5 preferred and convertible equity domain extension
- broader advanced research tooling after the core workstation product is trustworthy

---

## Next Steps

1. Treat provider trust, cockpit hardening, and shared run continuity as the active critical path.
2. Use the delivered Security Master seam as the base for governance productization instead of describing it as a future foundation wave.
3. Keep WPF migration work reinforcing workflow architecture rather than recreating page-local logic.
4. Pull testing and validation forward whenever workstation or governance surfaces expand.
