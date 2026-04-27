# Meridian - Opportunity Scan

**Last Updated:** 2026-04-27
**Status:** Repo-grounded opportunity scan aligned to the canonical roadmap, DK1 pilot sample-set/parity-packet evidence, generated-artifact retention cleanup, packet-bound sign-off preflight, cockpit readiness projection, WPF shell queue-button consumption of operator-inbox work items, and current WPF shell baseline including canonical `ResearchShell` launch routing, single-instance launch-argument forwarding, demo-data fixture semantics, Provider Health posture briefing, System Health triage, Notification Center filter recovery, Activity Log triage, Watchlist posture, Messaging Hub delivery posture, StrategyRuns filter recovery, and desktop workflow automation hardening

This document turns the current repository state into the next opportunity order. It is intentionally narrower than a full roadmap refresh: it identifies the best next opportunities, why they matter now, what they unlock, and where they belong in the active delivery sequence.

Use this with [`ROADMAP.md`](ROADMAP.md), [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md), [`IMPROVEMENTS.md`](IMPROVEMENTS.md), and [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md).

---

## Snapshot

Meridian's best opportunities are no longer broad platform-build items. The repo already shows a strong ingestion, storage, replay, export, execution, workstation, and Security Master baseline, plus concrete shared run, portfolio, ledger, and promotion seams. The highest-value opportunities now are the ones that remove trust ambiguity, harden operator workflows already in code, and keep the desktop shell consolidation tied to the same shared workflow model instead of drifting into a parallel UX program.

The current planning set is also more mature than the prior April 8 scan. As of 2026-04-27:

- the canonical roadmap treats Waves 1-4 as the core operator-readiness path
- the six-week plan narrows execution to DK1 provider-trust parity, paper-trading hardening, shared-model continuity, and the first deeper governance slices
- the DK1 evidence track now has a concrete Alpaca/Robinhood/Yahoo `pilotReplaySampleSet` emitted by the Wave 1 validation script plus checked-in packet generation, but generated provider-validation packets are no longer retained in git; the next operator review must start from a fresh date-stamped packet, and the sign-off helper can bind owner approvals to that reviewed packet and reject stale or copied sign-off files
- the feature inventory and implementation anchors show that shell-first WPF consolidation, the Trading desk briefing hero, Position Blotter selection review/action readiness, the Research desk briefing hero, Data Operations briefing state, Provider Health posture briefing, System Health triage, Notification Center filter recovery, Activity Log triage, Watchlist posture state, Messaging Hub delivery posture, StrategyRuns filter-aware recovery and run-scope presentation, shared workstation endpoints, canonical `ResearchShell` launch/deep-link handling, fixture/demo-mode cues, UI-automation page-state markers, corrected isolated desktop restore/build behavior, local single-instance mutex and launch-argument forwarding coverage, the Wave 2 trading-readiness contract with stable operator work-item IDs, the initial operator-inbox endpoint for readiness and reconciliation work items, WPF shell queue-button consumption of the primary operator work item, risk/control audit explainability projection, DK1 trust-gate readiness projection, canonical promotion approval-checklist state, and the Security Master baseline are materially present in the repo, but still not enough to call the end-to-end product finished

That means the opportunity order should stay delivery-aware: preserve the closed trust gate, finish the operator lane, unify the shared model, then deepen governance on top of the delivered baseline.

---

## Top Opportunities

### 1. Harden the existing paper-trading cockpit into a dependable operator lane

- **Category:** workflow completion
- **Gap:** The repo already exposes positions, orders, fills, risk, replay, sessions, promotion seams, a shared trading-readiness contract, stable operator work-item IDs, an initial operator-inbox endpoint that aggregates readiness and reconciliation work items with navigation targets, WPF shell queue-button consumption of the primary inbox item, a WPF Trading desk briefing hero, and Position Blotter selection review/action-readiness support that consumes the same execution and workflow signals. Current cockpit work now includes DK1 packet/sign-off projection, canonical approval-checklist requirements for `Backtest -> Paper` and `Paper -> Live` promotion review, audit-history refresh behavior, risk/control audit explainability warnings, local replay-audit metadata for consistency, compared evidence counts, timestamps, and mismatch reason, and stale-replay detection when active-session fill/order/ledger counts diverge after verification. The paper workflow still needs accepted operator scenarios around persistence, replay recovery, audit visibility, and end-to-end work-item handling in the primary UI.
- **User or operator value:** This converts Meridian from "paper trading is present" to "paper trading can be operated confidently."
- **Dependency it unlocks:** A credible `Backtest -> Paper` story and a safer foundation for any later `Paper -> Live` gate design.
- **Placement:** Critical path, Wave 2.

### 2. Make the shared run / portfolio / ledger model feel like one cross-workspace product

- **Category:** workflow completion
- **Gap:** `StrategyRunReadService`, `PortfolioReadService`, `LedgerReadService`, shared workstation endpoints, and promotion services are all in the repo, the Research desk briefing hero now routes selected runs into run-detail, portfolio, and paper-promotion review handoffs, and StrategyRuns now recovers when filters hide retained runs without reloading the run store. The product experience built on top of these seams is still uneven across `Research`, `Trading`, `Data Operations`, and `Governance`.
- **User or operator value:** A single run-centered model makes attribution, fills, positions, ledger, cash-flow, and reconciliation easier to follow and trust.
- **Dependency it unlocks:** Cleaner workstation continuity, less duplicated orchestration, and a more stable base for both WPF shell consolidation and governance productization.
- **Placement:** Critical path, Wave 3.

### 3. Productize governance and fund operations on top of the delivered Security Master baseline

- **Category:** flagship product capabilities
- **Gap:** Security Master is already the authoritative instrument-definition seam, and governance-facing APIs and workstation surfaces exist. The first governed report-pack artifact path and file-backed reconciliation break queue are now in code, but the next real product layer still needs deeper account/entity structure, multi-ledger, cash-flow, calibrated reconciliation casework, publication controls, and board/investor/compliance reporting workflows.
- **User or operator value:** This extends Meridian from trading-workstation credibility into front-, middle-, and back-office continuity built on one shared instrument and record model.
- **Dependency it unlocks:** Better reconciliations, stronger governed outputs, and a clearer fund-operations product story.
- **Placement:** Near-term strategic wave, Wave 4.

### 4. Close the DK1 pilot parity packet on top of the closed Wave 1 trust gate

- **Category:** provider readiness
- **Gap:** The active Wave 1 gate is now repo-closed, and DK1 has a concrete pilot replay/sample-set plus generated parity-packet tooling. Generated provider-validation packets are no longer retained in git, so the next review must regenerate or attach the date-stamped packet and bind owner approvals to that reviewed packet path, generated timestamp, status, sample/evidence counts, and explainability/calibration contract status. The open work is to keep the matrix, roadmap, generated runtime outputs, runbook, validation script, parity packet, trust rationale mapping, threshold calibration, and actual operator sign-off aligned around Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet L2 flush proof.
- **User or operator value:** Later cockpit, governance, and live-readiness claims stay credible when the trust gate remains explicit and reproducible.
- **Dependency it unlocks:** It preserves the evidence boundary that all later waves depend on and gives Wave 2 cockpit acceptance a concrete trust packet.
- **Placement:** Critical support track for DK1 and Waves 2-4.

### 5. Validate the delivered WPF shell baseline as workflow support, not a separate program

- **Category:** operator UX
- **Gap:** The repo now contains workspace shell pages, metadata-driven shell navigation, shared deep-page hosting, shell-context strips, canonical `ResearchShell` launch/deep-link handling, neutral demo-data fixture cues, UI-automation page-state markers, corrected isolated desktop restore/build behavior, local single-instance mutex and launch-argument forwarding coverage, and `MainPageViewModel`-anchored orchestration with smoke coverage plus focused tests for Batch Backtest, Position Blotter, Notification Center history recovery, Welcome, System Health triage, Activity Log triage, Watchlist posture states, workspace queue tone styles, shell context-strip behavior, Trading desk briefing hero states, Data Operations demo/provider posture, Provider Health posture states, and Research desk briefing hero promotion-review handoffs. That baseline should now be judged by whether it improves active Wave 2-4 workflows rather than by additional shell surface area.
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

1. Keep the closed Wave 1 provider-confidence gate as the first release gate, with the validation matrix, emitted DK1 `pilotReplaySampleSet`, freshly generated parity packet, packet-bound sign-off template, generated evidence attachments, and repo tests treated as the source of truth.
2. Define the paper-trading cockpit in operator terms: session persistence, replay confidence, audit trail visibility, and promotion review should be the acceptance center, not additional page count.
3. Use shared run, portfolio, ledger, and reconciliation seams as the required integration boundary for both workstation and governance work.
4. Treat WPF shell consolidation as successful only when it reduces workflow friction in active Wave 2-4 paths; the Trading desk briefing hero and Position Blotter selection-review rail should continue consuming shared execution/readiness state, and the Research desk briefing hero plus StrategyRuns recovery should continue consuming shared run/portfolio/promotion state rather than becoming separate shell-local models.
5. Keep Wave 5, Wave 6, and optional advanced tracks explicitly deferred until the Wave 2-4 operator-readiness path is materially closed on top of the preserved Wave 1 gate.
