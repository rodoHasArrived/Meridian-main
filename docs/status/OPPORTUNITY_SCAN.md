# Meridian - Opportunity Scan

**Last Updated:** 2026-04-08
**Status:** Repo-grounded opportunity scan aligned to the canonical April 8 roadmap refresh

This document turns the current repository state into the next opportunity order. The order here matches [`ROADMAP.md`](ROADMAP.md) exactly and uses the same core operator-readiness model.

---

## Snapshot

Meridian has crossed an important threshold: the repo already contains meaningful workstation flows in web and WPF, shared run / portfolio / ledger read models, promotion seams, a delivered Security Master baseline, reconciliation seams, and a strong data and storage foundation. The best opportunities now are the ones that remove ambiguity, close trust gaps, and turn already-implemented seams into dependable operator workflows.

---

## Opportunity Order

### 1. Wave 1: Provider confidence and checkpoint evidence

- **Category:** provider readiness
- **Gap:** Provider breadth is strong, but replay, runtime, auth, and checkpoint proof are still uneven across Polygon, Interactive Brokers, StockSharp, NYSE, and other under-validated paths.
- **User or operator value:** Operators can trust research, replay, and promotion decisions only when provider claims are backed by concrete evidence.
- **Dependency it unlocks:** Clean Wave 1 gates for cockpit hardening and later live-readiness work.
- **Placement:** Wave 1, critical path.

### 2. Wave 2: Paper-trading cockpit hardening

- **Category:** workflow completion
- **Gap:** The web trading cockpit has real surfaces for positions, orders, fills, replay, sessions, and promotion, but it still needs tighter acceptance criteria and stronger hardening.
- **User or operator value:** This turns a featureful cockpit into a dependable operating surface rather than an impressive demo path.
- **Dependency it unlocks:** Credible `Backtest -> Paper` workflow continuity and safer future live-readiness work.
- **Placement:** Wave 2, critical path.

### 3. Wave 3: Shared run / portfolio / ledger continuity

- **Category:** workflow completion
- **Gap:** Shared `StrategyRun`, portfolio, and ledger services exist, but not every workspace uses them with equal depth yet.
- **User or operator value:** One run-centered model makes research, trading, portfolio, ledger, cash-flow, and governance behavior easier to follow and trust.
- **Dependency it unlocks:** Cleaner cross-workspace UX and less duplicated orchestration logic.
- **Placement:** Wave 3, critical path.

### 4. Wave 4: Governance and fund-operations productization on top of the delivered Security Master baseline

- **Category:** flagship product capabilities
- **Gap:** Security Master is already the authoritative delivered baseline, but the workflows built on top of it still need account/entity, multi-ledger, cash-flow, reconciliation, and reporting depth.
- **User or operator value:** Instrument identity, classification, and economic-definition trust can now be turned into broader governance and fund-operations reliability.
- **Dependency it unlocks:** Better reconciliation quality, cleaner governed outputs, and a credible front-, middle-, and back-office story.
- **Placement:** Wave 4, near-term strategic wave.

### 5. Wave 5: Backtest Studio unification

- **Category:** research workflow depth
- **Gap:** Native and Lean results are both present, but they still behave like adjacent tools instead of one Backtest Studio experience.
- **User or operator value:** One coherent backtest workflow makes engine choice, comparison, and promotion review easier to understand.
- **Dependency it unlocks:** Cleaner research ergonomics after the shared model is stable enough to support it.
- **Placement:** Wave 5, after the core operator-readiness path.

### 6. Wave 6: Live integration readiness

- **Category:** live-readiness expansion
- **Gap:** Live-facing seams exist, but they should not be described as broadly ready until provider trust, paper workflow, and shared-model gates are materially closed.
- **User or operator value:** A measured live-readiness story is safer and more credible than a feature-count claim.
- **Dependency it unlocks:** A controlled, evidence-backed live integration narrative.
- **Placement:** Wave 6, after Waves 1-5.

### 7. Optional advanced research / scale tracks

- **Category:** advanced differentiation
- **Gap:** Deeper QuantScript workflow integration, queue-aware simulation, multi-instance coordination, and Phase 16 performance work all remain valuable but are not required for the core workstation product to feel finished.
- **User or operator value:** These tracks improve Meridian's ceiling once the core product story is already trustworthy and coherent.
- **Dependency it unlocks:** Longer-horizon differentiation rather than near-term operator-readiness.
- **Placement:** Optional follow-on work.

---

## Delivery Discipline Across Waves 1-4

- keep WPF and workstation cleanup work reinforcing shared read models and workflow services rather than recreating page-local logic
- pull testing and validation forward whenever workstation or governance surfaces expand
- keep Security Master framed as the delivered baseline and move open work to governance/product-depth follow-ons
- keep roadmap, status, and six-week planning docs synchronized so provider and readiness language does not drift

---

## Next Steps

1. Treat Wave 1 provider confidence, Wave 2 cockpit hardening, and Wave 3 shared-model continuity as the active critical path.
2. Use the delivered Security Master baseline as the base for Wave 4 governance productization instead of describing it as a future foundation seam.
3. Keep Wave 5, Wave 6, and optional advanced research / scale tracks explicitly deferred until the Wave 1-4 core operator-readiness path is materially closed.
4. Keep contradictions out of the planning set by checking retained completion claims against repo evidence before widening roadmap language.
