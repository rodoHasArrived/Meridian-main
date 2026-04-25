# Meridian - Opportunity Scan

**Last Updated:** 2026-04-25
**Status:** Repo-grounded opportunity scan aligned to the canonical roadmap, DK1 pilot sample-set evidence, and current WPF shell baseline

This document turns the current repository state into the next opportunity order. It is intentionally narrower than a full roadmap refresh: it identifies the best next opportunities, why they matter now, what they unlock, and where they belong in the active delivery sequence.

Use this with [`ROADMAP.md`](ROADMAP.md), [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md), [`IMPROVEMENTS.md`](IMPROVEMENTS.md), and [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md).

---

## Snapshot

Meridian's best opportunities are no longer broad platform-build items. The repo already shows a strong ingestion, storage, replay, export, execution, workstation, and Security Master baseline, plus concrete shared run, portfolio, ledger, and promotion seams. The highest-value opportunities now are the ones that remove trust ambiguity, harden operator workflows already in code, and keep the desktop shell consolidation tied to the same shared workflow model instead of drifting into a parallel UX program.

The current planning set is also more mature than the prior April 8 scan. As of 2026-04-25:

- the canonical roadmap treats Waves 1-4 as the core operator-readiness path
- the six-week plan narrows execution to DK1 provider-trust parity, paper-trading hardening, shared-model continuity, and the first deeper governance slices
- the DK1 evidence track now has a concrete Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` emitted by the Wave 1 validation script and referenced by the pilot parity runbook
- the feature inventory and implementation anchors show that shell-first WPF consolidation, shared workstation endpoints, and the Security Master baseline are materially present in the repo, but still not enough to call the end-to-end product finished

That means the opportunity order should stay delivery-aware: preserve the closed trust gate, finish the operator lane, unify the shared model, then deepen governance on top of the delivered baseline.

---

## Top Opportunities

### 1. Harden the existing paper-trading cockpit into a dependable operator lane

- **Category:** workflow completion
- **Gap:** The repo already exposes positions, orders, fills, risk, replay, sessions, and promotion seams, and current cockpit work is tightening promotion rejection outcomes and audit-history refresh. The paper workflow still needs clearer daily-use acceptance criteria, broader audit visibility, and tighter behavior around persistence and replay recovery.
- **User or operator value:** This converts Meridian from "paper trading is present" to "paper trading can be operated confidently."
- **Dependency it unlocks:** A credible `Backtest -> Paper` story and a safer foundation for any later `Paper -> Live` gate design.
- **Placement:** Critical path, Wave 2.

### 2. Make the shared run / portfolio / ledger model feel like one cross-workspace product

- **Category:** workflow completion
- **Gap:** `StrategyRunReadService`, `PortfolioReadService`, `LedgerReadService`, shared workstation endpoints, and promotion services are all in the repo, but the product experience built on top of them is still uneven across `Research`, `Trading`, `Data Operations`, and `Governance`.
- **User or operator value:** A single run-centered model makes attribution, fills, positions, ledger, cash-flow, and reconciliation easier to follow and trust.
- **Dependency it unlocks:** Cleaner workstation continuity, less duplicated orchestration, and a more stable base for both WPF shell consolidation and governance productization.
- **Placement:** Critical path, Wave 3.

### 3. Productize governance and fund operations on top of the delivered Security Master baseline

- **Category:** flagship product capabilities
- **Gap:** Security Master is already the authoritative instrument-definition seam, and governance-facing APIs and workstation surfaces exist, but the next real product layer still needs deeper account/entity structure, multi-ledger, cash-flow, reconciliation, and governed-output workflows.
- **User or operator value:** This extends Meridian from trading-workstation credibility into front-, middle-, and back-office continuity built on one shared instrument and record model.
- **Dependency it unlocks:** Better reconciliations, stronger governed outputs, and a clearer fund-operations product story.
- **Placement:** Near-term strategic wave, Wave 4.

### 4. Close the DK1 pilot parity packet on top of the closed Wave 1 trust gate

- **Category:** provider readiness
- **Gap:** The active Wave 1 gate is now repo-closed, and DK1 has a concrete pilot replay/sample-set contract. The open work is to keep the matrix, roadmap, runtime artifacts, runbook, validation script, trust rationale mapping, threshold calibration, and operator sign-off aligned around Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet L2 flush proof.
- **User or operator value:** Later cockpit, governance, and live-readiness claims stay credible when the trust gate remains explicit and reproducible.
- **Dependency it unlocks:** It preserves the evidence boundary that all later waves depend on and gives Wave 2 cockpit acceptance a concrete trust packet.
- **Placement:** Critical support track for DK1 and Waves 2-4.

### 5. Validate the delivered WPF shell baseline as workflow support, not a separate program

- **Category:** operator UX
- **Gap:** The repo now contains workspace shell pages, metadata-driven shell navigation, shared deep-page hosting, shell-context strips, and `MainPageViewModel`-anchored orchestration with smoke coverage. That baseline should now be judged by whether it improves active Wave 2-4 workflows rather than by additional shell surface area.
- **User or operator value:** Operators benefit when the desktop shell clarifies high-traffic tasks and preserves trust-state cues instead of becoming a second workstation model.
- **Dependency it unlocks:** Confident desktop consolidation that reinforces the same run-centered and governance-centered seams already used elsewhere.
- **Placement:** Supporting track inside Waves 2-4, not an independent roadmap wave.

### 6. Unify native and Lean backtesting only after the shared model is stable enough

- **Category:** flagship product capabilities
- **Gap:** Native and Lean workflows both exist, but unifying them into one Backtest Studio still depends on stronger run continuity, comparison depth, and operator trust in the active workstation model.
- **User or operator value:** One backtesting experience reduces engine-specific mental overhead and makes review, comparison, and promotion easier.
- **Dependency it unlocks:** A stronger research workflow after the core operator path is already coherent.
- **Placement:** Later wave, Wave 5.

### 7. Keep live integration readiness explicit and tightly bounded

- **Category:** reliability and observability
- **Gap:** Live-facing seams are present, but broader live-readiness language would still outrun provider evidence, paper-trading hardening, and controlled approval/audit flows.
- **User or operator value:** A measured live story protects operator trust better than broad readiness claims.
- **Dependency it unlocks:** A controlled, evidence-backed brokerage-readiness narrative.
- **Placement:** Later wave, Wave 6.

### 8. Defer advanced research and scale tracks until the core workstation is trustworthy

- **Category:** architecture simplification
- **Gap:** QuantScript expansion, queue-aware simulation, multi-instance coordination, and later performance work may all matter, but they do not close the current product-readiness gap.
- **User or operator value:** Focus stays on the workflows operators actually need to trust first.
- **Dependency it unlocks:** Cleaner prioritization and less roadmap sprawl.
- **Placement:** Optional track after Waves 1-6 core sequencing.

---

## Why These Matter Now

The current repo state shows a product that is beyond foundations but not yet beyond proof. The opportunity order therefore should optimize for four things:

1. remove ambiguity in what Meridian can responsibly claim today
2. harden workflows operators can already see and exercise
3. keep shared read models and Security Master as the center of gravity
4. avoid splitting web, WPF, governance, and research work into parallel subsystems

That is why Wave 2 cockpit hardening now outranks additional surface expansion, while the closed Wave 1 trust gate should still be treated as the evidence boundary for the core product path.

---

## Recommended Next Steps

1. Keep the closed Wave 1 provider-confidence gate as the first release gate, with the validation matrix, emitted DK1 `pilotReplaySampleSet`, evidence artifacts, and repo tests treated as the source of truth.
2. Define the paper-trading cockpit in operator terms: session persistence, replay confidence, audit trail visibility, and promotion review should be the acceptance center, not additional page count.
3. Use shared run, portfolio, ledger, and reconciliation seams as the required integration boundary for both workstation and governance work.
4. Treat WPF shell consolidation as successful only when it reduces workflow friction in active Wave 2-4 paths.
5. Keep Wave 5, Wave 6, and optional advanced tracks explicitly deferred until the Wave 2-4 operator-readiness path is materially closed on top of the preserved Wave 1 gate.
