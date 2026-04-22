# Kernel Readiness Dashboard (DK Program)

**Last Updated:** 2026-04-20  
**Program Scope:** Delivery Kernel waves DK1-DK2 mapped to Waves 2-4 in [`ROADMAP.md`](ROADMAP.md)  
**Purpose:** single hand-authored status dashboard for subsystem kernel readiness, gate progression, implementation commitments, and rollback posture.

---

## Dashboard Legend

- **Wave:** DK1 (data-quality + provider trust) or DK2 (promotion + export + reconciliation)
- **Gate Status:** ✅ pass | 🟡 in progress | ⛔ blocked | ⚪ not started
- **Kernel Readiness:** Ready | At Risk | Blocked | Not Started

---

## Program Cadence and Operating Window

- **Cadence:** weekly subsystem review (Mon), cross-subsystem interop review (Wed), operator-readiness review (Fri)
- **Operating window:** 2026-04-20 through 2026-06-26
- **Current commitment window:** 2026-04-20 through 2026-05-29
- **Status publication rule:** update this dashboard at least once per week; release-governance decisions reference this file plus `provider-validation-matrix.md`

---

## Subsystem Readiness Board

| Subsystem | Wave | Owner | Parity | Explainability | Calibration | Operator Sign-off | Kernel Readiness | Next Milestone (Target Date) | Evidence / Notes |
|---|---|---|---|---|---|---|---|---|---|
| Data quality + provider trust | DK1 | Data Operations & Provider Reliability owner | 🟡 | 🟡 | ⚪ | ⚪ | At Risk | Complete pilot parity runbook for Alpaca/Robinhood/Yahoo set (2026-05-01) | Must stay synchronized with provider validation matrix and Wave 1 scripts |
| Promotion + paper-trading cockpit | DK1 -> DK2 handoff | Trading Workstation owner | ⚪ | ⚪ | ⚪ | ⚪ | Not Started | Lock promotion-path audit fields + pilot review checklist (2026-05-08) | Depends on DK1 trust explainability and calibrated thresholds |
| Export + packaging | DK2 | Data Operations Export owner | ⚪ | ⚪ | ⚪ | ⚪ | Not Started | Freeze export schema/version contract for governed pilot outputs (2026-05-15) | Must remain aligned with shared run/portfolio/ledger contract versions |
| Reconciliation + governance | DK2 | Governance/Fund Ops owner | ⚪ | ⚪ | ⚪ | ⚪ | Not Started | Approve reconciliation tolerance profile + exception playbook (2026-05-22) | Requires promotion/export lineage continuity from DK2 |
| Shared run/portfolio/ledger interop contracts | Cross-wave (DK1 + DK2) | Shared Platform Interop owner (Architecture + Contracts) | 🟡 | 🟡 | 🟡 | ⚪ | At Risk | Publish compatibility matrix + contract-change review cadence (2026-04-29) | Governs DTO/API compatibility across Trading, Data Ops, Governance |

---

## Current Implementation Commitments (Wave-Aligned)

| Window | Subsystem | Implemented commitment | Acceptance artifact |
|---|---|---|---|
| 2026-04-20 -> 2026-05-01 | Data quality + provider trust (DK1) | Standardize parity-runbook structure and replay/sample set for DK1 pilot scope | Updated Wave 1 validation script output + dashboard evidence links |
| 2026-04-20 -> 2026-05-01 | Shared interop contracts (cross-wave) | Establish interop contract board, versioning policy, and compatibility matrix template | Contract compatibility matrix committed and linked from dashboard |
| 2026-05-02 -> 2026-05-15 | Promotion + paper cockpit (DK1/DK2) | Implement promotion rationale fields and operator approval checklist coverage | Promotion flow audit sample with end-to-end traceability |
| 2026-05-09 -> 2026-05-22 | Export + packaging (DK2) | Freeze governed export schema and add version-compatibility checks | Export contract validation report for pilot scenarios |
| 2026-05-16 -> 2026-05-29 | Reconciliation + governance (DK2) | Implement calibrated tolerance profile and exception severity routing | Reconciliation exception burn-down and tolerance calibration summary |

---

## Entry/Exit Gate Checklist by Wave

### DK1 - Data quality + provider trust

#### Entry checklist

- [x] **Parity entry:** Wave 1 closure evidence remains current and reproducible.
- [ ] **Explainability entry:** trust signals have operator-visible source and rationale.
- [ ] **Calibration entry:** baseline trust thresholds are declared for pilot operations.
- [ ] **Operator entry:** Data Ops + Trading approve DK1 pilot scope.

#### Exit checklist

- [ ] **Parity pass:** cockpit views match validated provider/replay results for pilot scenarios.
- [ ] **Explainability pass:** every trust alert maps to source, reason code, and operator action.
- [ ] **Calibration pass:** thresholds tuned with documented false-positive/false-negative review.
- [ ] **Operator sign-off:** Data Ops + Trading owners approve DK1 completion.

### DK2 - Promotion + export + reconciliation

#### Entry checklist

- [ ] **Parity entry:** DK1 is signed and shared contract seams are active.
- [ ] **Explainability entry:** promotion/export flows emit audit rationale.
- [ ] **Calibration entry:** reconciliation tolerances and severities are defined.
- [ ] **Operator entry:** Trading + Governance approve DK2 pilot playbook.

#### Exit checklist

- [ ] **Parity pass:** promoted run, export, and reconciliation outputs agree across workstation/API/governance views.
- [ ] **Explainability pass:** end-to-end trace from trusted input to governed output is operator-visible.
- [ ] **Calibration pass:** reconciliation exceptions are tuned with no unresolved critical mismatches.
- [ ] **Operator sign-off:** Trading + Governance owners sign DK2 readiness.

---

## Risk Register and Rollback Tracker

| Subsystem | Active Risk | Indicator | Current Mitigation | Rollback Trigger | Rollback Plan |
|---|---|---|---|---|---|
| Data quality + provider trust | validation/script and operator UI trust drift | unresolved trust alert deltas across script vs UI outputs | weekly matrix/script/doc sync check | two consecutive unresolved drift reports | pin last verified matrix + replay baseline, pause promotion expansion, rerun DK1 calibration |
| Promotion + paper cockpit | inconsistent approval state across UI/API | audit mismatch in promotion chain | shared promotion state schema review | any critical promotion audit mismatch | roll back to last signed promotion workflow contract, disable new lanes by feature flag |
| Export + packaging | schema/version drift in governed exports | export validation failures or missing lineage | contract version freeze for pilot | repeated export validation failures on signed scenarios | revert exporter version, regenerate from last good run snapshots |
| Reconciliation + governance | tolerance miscalibration causing false flood or miss | sustained unresolved critical exception count | staged tolerance tuning with governance review | unresolved critical exception threshold breach | restore prior tolerance profile, reprocess affected window, require manual approvals |
| Shared interop contracts | incompatible DTO/API changes between subsystems | cross-workspace contract test failures | compatibility matrix + contract review board | any contract-breaking change without approved migration | revert shared contract version/API shape and block downstream deploys |

---

## Alignment Notes (Waves 2-4)

- **Wave 2 (cockpit hardening):** execution tracked through DK1 trust and explainability gates.
- **Wave 3 (shared-model continuity):** split between DK1 trust dependencies and DK2 promotion/export/reconciliation continuity.
- **Wave 4 (governance productization):** readiness claim requires DK2 exit and Governance operator sign-off.

This dashboard is the single status surface for DK program tracking. Use it with [`ROADMAP.md`](ROADMAP.md) and [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) to avoid duplicate or parallel migration plans.
