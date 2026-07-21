# 2026-07 First-Order Improvement Slate (Ranked W9 Priorities)

**Status:** accepted planning input; live status lives in the roadmap registry
**Owner:** core-team
**Reviewed:** 2026-07-21
**Registry decision:** `DEC-PRIORITY-SLATE-001` in [`docs/roadmap/data/decision-log.yml`](../roadmap/data/decision-log.yml)
**Registry rows:** `W9-TRUTH-001` through `W9-INGEST-009` in [`docs/roadmap/data/roadmap-items.yml`](../roadmap/data/roadmap-items.yml)

This document records the ranked first-order improvement slate adopted on 2026-07-21 and maps each
rank to its durable roadmap row and to the production-readiness rows it strengthens. Treat the
roadmap registry as live truth; this page preserves the ranking rationale so the ordering decision
stays explainable.

## Snapshot

The slate answers one question: **what most improves Meridian's ability to prove, book, reconcile,
approve, and report a number for a real evaluator and a real fund-ops team, in order?** The ordering
is truth-and-evaluation first, then promotion-gate integrity and the live feedback loop, then
fund-accounting deliverables, then safety and governance hardening, then institutional ingestion.

## Ranked Slate

| # | Roadmap row | Improvement | Why it's first-order |
| -- | --- | --- | --- |
| 1 | `W9-TRUTH-001` | Loud, fail-closed handling of simulated data and in-memory persistence | Fake-looking-real is fatal for a "prove the number" product |
| 2 | `W9-DEMO-002` | One-command seeded demo with durable storage | The first hour currently ends in an empty screen; nothing else matters if evaluation fails |
| 3 | `W9-PAPER-003` | Paper-trading realism (limit/stop matching, costs, no $1 fills) | The promotion gate currently launders overfit strategies |
| 4 | `W9-ALPACA-004` | Alpaca fill streaming | The only turnkey live venue has a broken feedback loop |
| 5 | `W9-REPORT-005` | Client-grade PDF/XLSX + partners'-capital statement | Ops teams currently re-type every deliverable into Excel |
| 6 | `W9-NAV-006` | Unitized NAV + real fee/waterfall/capital-call economics | The hard math a fund accountant needs still lives in Excel |
| 7 | `W9-SAFETY-007` | Kill-switch cancel-all + fat-finger/notional/collar rules; wire or demote WPF safety buttons | Safety surfaces must never overpromise |
| 8 | `W9-GOV-008` | Route-level authorization coverage + fail-closed tenancy + hash-chained accounting audit | Governance is the brand; these are the gaps in it |
| 9 | `W9-INGEST-009` | Institutional file ingestion (camt.053/BAI2) + sided reconciliation matcher | Reconciliation value is capped by what can be ingested and trusted |

## Ordering Rationale

1. **Truth before demonstration (ranks 1–2).** A prove-the-number product cannot tolerate
   simulated data that reads as real, and it cannot be evaluated at all if the first hour ends in an
   empty screen. Rank 1 makes simulation labeling and fail-closed persistence selection mandatory;
   rank 2 builds the seeded durable demo on top of that labeling so the demo is loud about being a
   demo. Rank 2 depends on rank 1's labeling contract.
2. **Honest gates before more surface (ranks 3–4).** The paper-to-live promotion pipeline is the
   product's core loop. Rank 3 stops paper sessions from overstating live viability; rank 4 closes
   the live feedback loop on the one turnkey venue so live evidence is as trustworthy as paper
   evidence claims to be.
3. **The deliverable is the product (ranks 5–6).** Fund-ops teams judge the platform by what they
   can hand to a client or auditor. Rank 5 makes governed report packs export client-grade PDF/XLSX
   including a partners-capital statement; rank 6 supplies the ledger-backed economics (unitized
   NAV, fees, waterfalls, capital calls) that statement needs to be right. Rank 5 and rank 6
   reconcile against each other.
4. **Never overpromise safety or governance (ranks 7–8).** Safety buttons that do nothing and
   governance gaps in authorization, tenancy, and audit chaining are brand damage in this market.
   These harden what already exists rather than adding surface.
5. **Widen the trusted intake last (rank 9).** camt.053/BAI2 ingestion and a sided matcher extend
   the delivered `W5X-CONNECT-001` connector seam; sequencing it after truth, gates, and
   deliverables keeps new intake from outrunning the trust machinery.

## Production-Readiness Tracker Mapping

Each slate row strengthens named rows in the
[Implementation and Readiness Tracker](implementation-todo-list.md); the slate does not replace that
tracker's release-gate semantics.

| Roadmap row | Related tracker rows |
| --- | --- |
| `W9-TRUTH-001` | `PRD-000`, `PRD-005`, `PRD-007`, `PRD-012` (no fake-real output; fail-closed production composition) |
| `W9-DEMO-002` | `PRD-000`, `PRD-013` (supported startup posture proves out through the seeded demo path) |
| `W9-PAPER-003` | `PRD-006` (order truth), promotion evidence quality behind `W7-LIVE-001` |
| `W9-ALPACA-004` | `PRD-006` (execution-report lifecycle consumption, reconnect recovery) |
| `W9-REPORT-005` | `PRD-005`, `PRD-017` (authoritative as-of outputs; evidence-chained artifacts) |
| `W9-NAV-006` | `PRD-003`, `PRD-004` (governed posting boundary and close-safety for economics postings) |
| `W9-SAFETY-007` | `PRD-006` (operator-control state, breaker posture), WPF surface honesty under `PRD-100`/`PRD-110` |
| `W9-GOV-008` | `PRD-001`, `PRD-007`, `PRD-009` (authorization, tenancy, durable tamper-evident audit) |
| `W9-INGEST-009` | `PRD-010` (bounded ingress), `PRD-101` (deterministic reconciliation semantics) |

## What This Slate Is Not

- It is not a completion claim: every W9 row enters the registry as `planned` with
  `planned_evidence` posture.
- It is not a replacement for the production-readiness tracker's P0 release gate; `P0` rows remain
  release blockers regardless of slate order.
- It does not reopen deferred lanes (mobile, client portal, forecasting, enterprise risk,
  no-code workflow designer); every row strengthens data confidence, reconciliation, approvals,
  accounting records, retained evidence, workflow controls, governed reporting, or the bounded
  live-readiness envelope, consistent with the program scope gate.

## Validation

```bash
python3 build/scripts/docs/validate-roadmap-registry.py --summary
python3 build/scripts/docs/render-roadmap-docs.py --summary
python3 build/scripts/docs/validate-docs-structure.py --summary
```
