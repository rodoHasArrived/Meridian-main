# Meridian - Now / Next / Later Roadmap

**Generated:** 2026-03-25
**Refreshed:** 2026-04-27
**Format:** Now / Next / Later compatibility view
**Basis:** `ROADMAP.md`, `PROGRAM_STATE.md`, `FEATURE_INVENTORY.md`, `production-status.md`, `kernel-readiness-dashboard.md`, provider validation evidence, generated-artifact retention cleanup, packet-bound DK1 sign-off preflight, cockpit readiness projection, stable operator work-item IDs, initial operator-inbox endpoint evidence with WPF shell queue-button consumption, Position Blotter selection review/action-readiness evidence, Data Operations hero, Provider Health posture, System Health triage evidence, Notification Center filter-recovery evidence, Activity Log triage evidence, Watchlist posture evidence, Messaging Hub delivery-posture evidence, StrategyRuns filter-recovery evidence, canonical workspace launch/deep-link routing, single-instance launch-argument forwarding, workflow automation hardening, demo-data fixture semantics, and active workstation/governance plans as of 2026-04-27
**Status:** Refreshed compatibility view. `ROADMAP.md` and `PROGRAM_STATE.md` remain the canonical wave and status sources.

This document preserves the older Now / Next / Later shape for quick reading, but it no longer defines a separate roadmap. The active roadmap is the Wave 1-6 model in [`ROADMAP.md`](ROADMAP.md), with status labels and target dates sourced from [`PROGRAM_STATE.md`](PROGRAM_STATE.md).

---

## How to Read This

| Horizon | Meaning |
| --------- | --------- |
| **Now** | Active delivery or active maintenance that directly supports core operator-readiness |
| **Next** | Sequenced work that follows the Wave 2-4 operator-readiness path |
| **Later** | Optional or later-wave work that should not distract from the current gates |

---

## Current Program State

| Wave | Status | Current meaning |
| --- | --- | --- |
| Wave 1 | Done | Closed provider-confidence, checkpoint, and Parquet evidence gate; keep synchronized through the matrix, validation script, DK1 sample-set evidence, freshly generated date-stamped parity packet, packet-bound sign-off template, and pending operator sign-off. |
| Wave 2 | In Progress | Harden the existing paper-trading cockpit into a dependable operator workflow. |
| Wave 3 | In Progress | Make shared run, portfolio, ledger, cash-flow, and reconciliation continuity feel like one product model. |
| Wave 4 | In Progress | Productize governance and fund operations on top of the delivered Security Master baseline. |
| Wave 5 | Planned | Unify native and Lean backtesting into one Backtest Studio after the shared model is stable enough. |
| Wave 6 | Planned | Expand controlled live integration readiness after trust and paper-workflow gates are materially closed. |

---

## Now

### 1. Wave 1 Trust-Gate Maintenance and DK1 Pilot Parity

Wave 1 is no longer an open broad provider push. The active gate is closed around Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet L2 flush proof.

**Delivery scope:**

- keep Alpaca and Yahoo repo-closed through executable test evidence
- keep Robinhood explicitly bounded by broker-session runtime evidence regenerated or attached for the review run
- keep Polygon, Interactive Brokers, NYSE, and StockSharp outside the active Wave 1 closure claim unless the matrix is intentionally widened
- keep checkpoint reliability and Parquet L2 flush behavior tied to passing repo tests
- keep `scripts/dev/run-wave1-provider-validation.ps1`, `scripts/dev/generate-dk1-pilot-parity-packet.ps1`, `scripts/dev/prepare-dk1-operator-signoff.ps1`, `provider-validation-matrix.md`, generated validation summaries, date-stamped parity packets, packet-bound sign-off templates, the DK1 pilot parity runbook, and the cockpit readiness projection synchronized around the emitted `pilotReplaySampleSet`
- keep the WPF Data Operations hero and Provider Health posture briefing aligned to shared provider, backfill, storage, session, and export state so they support operator handoffs without becoming separate readiness sources

**Exit gate:** The matrix, roadmap, validation script output, DK1 runbook, generated parity packet, packet-bound sign-off template, dashboard, and operator sign-off all describe the same active provider set and pilot replay/sample contract.

---

### 2. Wave 2 Paper-Trading Cockpit Hardening

The execution, paper-session, promotion, risk, and brokerage seams are already in code. The WPF Trading shell also has a desk briefing hero that routes context-required, replay-mismatch, replay-stale, controls-blocked, paper-review, and live-oversight states from shared readiness inputs. The current gap is still operator dependability: restart safety, replay confidence and freshness, audit visibility, and promotion review clarity.

**Delivery scope:**

- harden positions, orders, fills, sessions, replay, risk, and promotion flows already exposed through `/api/execution/*` and `/api/promotion/*`
- keep `/api/workstation/trading/readiness` as the shared acceptance contract for paper-session state, replay consistency and freshness, execution controls, recent risk/control audit evidence, missing-field explainability warnings, promotion checklist state, brokerage-sync posture, and stable operator work items; use `/api/workstation/operator/inbox` plus the WPF shell queue button as the initial shared aggregation and navigation contract for readiness and reconciliation work items while end-to-end workflow acceptance remains open
- use the local replay-audit metadata slice for durable readiness reconstruction of replay consistency, compared evidence counts, timestamps, primary mismatch reason, and stale replay coverage after active-session fill/order/ledger counts diverge
- keep the Trading desk briefing hero and Position Blotter selection-review rail aligned to the shared active-run, workflow-summary, and operator-readiness lane instead of treating them as separate readiness sources
- keep `Backtest -> Paper` explicit, auditable, and reconstructable after restart
- preserve operator rationale and blocking reasons for approvals and rejections
- tie cockpit readiness to DK1 trust evidence instead of page count

**Exit gate:** An operator can create, restore, verify, close, replay, and review a paper session with promotion evidence visible from one coherent workflow.

---

### 3. Wave 3 Shared Run / Portfolio / Ledger Continuity

The shared run, portfolio, ledger, cash-flow, and reconciliation services exist. The product work is to make them the default cross-workspace system of record. The WPF Research shell now has a desk briefing hero that routes selected runs into run-detail, portfolio, and paper-promotion review handoffs from shared workstation data, so this is support evidence for Wave 3 rather than a separate research roadmap.

**Delivery scope:**

- deepen run history across backtest and paper workflows
- connect fills, attribution, positions, ledger, cash-flow, and reconciliation through one run-centered model
- keep WPF shell context, related-workflow routing, Research run/promotion handoffs, and API summaries aligned to the same shared seams
- keep fallback/fixture payloads clearly separate from readiness claims

**Exit gate:** Research, Trading, and Governance drill-ins use the same run-continuity seam and show explicit lineage, warning, and handoff context.

---

### 4. Wave 4 Governance and Fund-Operations Productization

Security Master is now a delivered baseline, not a future standalone wave. Wave 4 is the productization work on top of that baseline.

**Delivery scope:**

- deepen account/entity, strategy-structure, multi-ledger, cash-flow, reconciliation, and report-pack workflows
- keep Security Master as the authoritative instrument source for governance DTOs and services
- extend the delivered file-backed reconciliation break queue and governed report-pack artifact path into full durable casework, calibrated exception routing, and publishable governed outputs
- reuse shared run, portfolio, ledger, export, and Security Master reference seams instead of creating a parallel governance stack

**Exit gate:** Governance behaves like an operator workflow with durable queues, review surfaces, provenance, and publishable outputs.

---

### 5. WPF Workflow-First Validation

The WPF workspace shell baseline is present in code: workspace shell pages, `ShellNavigationCatalog`, `MainPageViewModel` orchestration, `DesktopLaunchArguments` startup/deep-link parsing, deep-page hosting, shell-context strips, the Trading desk briefing hero, Position Blotter selection review/action readiness, the Research desk briefing hero, the Data Operations desk briefing hero, Provider Health posture briefing, System Health triage briefing, Notification Center filter recovery, Activity Log triage strip, Watchlist posture strip, Messaging Hub delivery posture, StrategyRuns filter-aware recovery/run-scope presentation, operator queue button state, neutral demo-data fixture cues, single-instance launch-argument forwarding, and shell/navigation smoke coverage.

This is a supporting track inside Waves 2-4, not a separate roadmap wave.

**Delivery scope:**

- validate the delivered shell/navigation baseline against high-traffic Wave 2-4 workflows
- keep scripted workflow launch, screenshot confirmation, and fixture/demo mode aligned to `ResearchShell`, `TradingShell`, `DataOperationsShell`, and `GovernanceShell`
- keep page-body harmonization focused on cockpit, shared-model, and governance value
- continue MVVM extraction where it removes active workflow friction
- avoid widening the shell into a parallel workstation model

**Exit gate:** The desktop shell improves active operator workflows and stays aligned with the same shared read-model seams used by the retained workstation APIs.

---

## Next

### 6. Wave 5 Backtest Studio Unification

Native backtesting and Lean integration both exist, but unifying them should follow stronger Wave 2-4 continuity.

**Delivery scope:**

- unify native and Lean result models
- improve run comparison and diff workflows
- broaden fill-model realism where it materially affects operator trust
- keep research workflow improvements tied to shared run history and promotion readiness

**Exit gate:** Backtesting feels like one coherent workflow regardless of engine.

---

### 7. Wave 6 Controlled Live Integration Readiness

Live integration should follow validated provider evidence and a dependable paper workflow.

**Delivery scope:**

- validate at least one broader brokerage path against a real vendor surface
- add execution audit trail and human approval controls fit for live-adjacent operation
- define safe `Paper -> Live` promotion gates
- formalize manual overrides, circuit breakers, and intervention flows

**Exit gate:** Meridian has a controlled, evidence-backed live-readiness story without overclaiming broad live-trading completion.

---

## Later

### 8. Optional Advanced Research and Scale

These tracks deepen Meridian's ceiling after the core workstation product is trustworthy and coherent.

- deeper QuantScript sample libraries and workflow integration
- L3 inference and queue-aware execution simulation
- multi-instance coordination and distributed collector ownership
- optional assembly/performance optimization work
- preferred and convertible equity follow-ons after the active governance and shared-model lanes stabilize

---

## Core Operator-Readiness Gates

Meridian can reasonably claim core operator-readiness when all four active gates are green:

1. **Wave 1:** the active provider gate for Alpaca, Robinhood, and Yahoo is documented in executable suites plus generated runtime attachments for bounded scenarios, checkpoint and Parquet proof remain closed, and the Wave 1 validation script reproduces the offline gate.
2. **Wave 2:** the workstation exposes a dependable paper-trading cockpit, not just endpoint coverage or partial UI, and `Backtest -> Paper` is explicit and auditable.
3. **Wave 3:** run history, portfolio, fills, attribution, ledger, cash-flow, and reconciliation views are connected through one shared model across backtest and paper flows.
4. **Wave 4:** Security Master remains operator-accessible and governance has concrete account/entity, multi-ledger, cash-flow, reconciliation, and reporting seams built on shared contracts.

Waves 5 and 6 deepen the product afterward. They are not prerequisites for the core operator-ready baseline.

---

## Risks

- Provider language can drift back toward broad live-readiness unless the Wave 1 matrix stays explicit.
- Cockpit polish can outrun session, replay, and promotion audit evidence.
- WPF shell work can become a parallel UX program unless it is judged by Wave 2-4 workflow value.
- Governance can sprawl if it does not stay anchored to Security Master, shared read models, and durable casework/output seams.
- Live-readiness claims should stay downstream of proven paper-workflow and provider-trust gates.

---

## Reference Documents

- [`ROADMAP.md`](ROADMAP.md)
- [`PROGRAM_STATE.md`](PROGRAM_STATE.md)
- [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md)
- [`production-status.md`](production-status.md)
- [`provider-validation-matrix.md`](provider-validation-matrix.md)
- [`kernel-readiness-dashboard.md`](kernel-readiness-dashboard.md)
- [`FEATURE_INVENTORY.md`](FEATURE_INVENTORY.md)
- [`../plans/meridian-6-week-roadmap.md`](../plans/meridian-6-week-roadmap.md)
- [`../plans/waves-2-4-operator-readiness-addendum.md`](../plans/waves-2-4-operator-readiness-addendum.md)
- [`../plans/trading-workstation-migration-blueprint.md`](../plans/trading-workstation-migration-blueprint.md)
- [`../plans/governance-fund-ops-blueprint.md`](../plans/governance-fund-ops-blueprint.md)
