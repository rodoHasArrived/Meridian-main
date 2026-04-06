# Meridian - Opportunity Scan

**Last Updated:** 2026-04-04
**Status:** Repo-grounded opportunity scan aligned to the current roadmap refresh

This document turns the current repository state into prioritized opportunities. It is meant to be additive to [`ROADMAP.md`](ROADMAP.md), not a duplicate backlog.

---

## Snapshot

Meridian has crossed an important threshold: the repo now contains meaningful workstation flows in web and WPF, shared run and ledger read models, Security Master visibility, reconciliation seams, and a strong data and storage baseline. The best opportunities are therefore no longer "add another broad foundation." The best opportunities now are the ones that remove ambiguity, close trust gaps, and turn already-implemented seams into finished operator workflows.

---

## Top Opportunities

### 1. Close the provider-trust gap

- **Category:** provider readiness
- **Gap:** Provider breadth is strong, but replay/runtime evidence is still uneven across Polygon, Interactive Brokers, StockSharp, and NYSE.
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

### 4. Elevate Security Master from visible feature to authoritative platform seam

- **Category:** flagship product capabilities
- **Gap:** Security Master is now visible in governance workflows, but it is not yet the consistent instrument layer across research, trading, portfolio, ledger, reconciliation, and reporting.
- **User or operator value:** Instrument identity, classification, and economic-definition trust improve every downstream governance and portfolio workflow.
- **Dependency it unlocks:** Better reconciliation quality, cleaner reporting, and stronger governance data joins.
- **Placement:** Near-term wave.

### 5. Productize governance workflows on top of shared contracts

- **Category:** flagship product capabilities
- **Gap:** Governance now has concrete seams, but multi-ledger, cash-flow, break-queue, and report-pack workflows are still early slices rather than finished product paths.
- **User or operator value:** This is what makes Meridian feel like a fund-operations platform instead of only a strategy or execution tool.
- **Dependency it unlocks:** A differentiated front-, middle-, and back-office story built on one platform.
- **Placement:** Near-term wave.

### 6. Keep workstation delivery architecture-simple

- **Category:** architecture simplification
- **Gap:** The product surface is broad enough that page-local orchestration could easily creep back in, especially in WPF migration work.
- **User or operator value:** Cleaner seams reduce regressions and make the product easier to evolve across both web and desktop.
- **Dependency it unlocks:** Faster delivery across later workstation and governance waves.
- **Placement:** Continuous supporting track.

### 7. Strengthen operator-facing testing and validation

- **Category:** testing and validation
- **Gap:** Workstation flows have grown faster than the acceptance story around cross-workspace behavior, replay confidence, and operator-grade regression coverage.
- **User or operator value:** Better validation reduces the chance that Meridian looks connected but behaves inconsistently under real use.
- **Dependency it unlocks:** Safer roadmap execution and stronger pilot credibility.
- **Placement:** Critical-path support track.

### 8. Preserve WPF momentum without letting it become parallel UX debt

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
- broader advanced research tooling after the core workstation product is trustworthy

---

## Next Steps

1. Treat provider trust, cockpit hardening, and shared run continuity as the active critical path.
2. Keep Security Master and governance work tied to shared DTOs, read models, and export seams.
3. Use WPF migration work to reinforce workflow architecture, not to recreate page-local logic.
4. Pull testing and validation forward whenever workstation or governance surfaces expand.
