# Kernel Readiness Dashboard (DK Program)

**Last Updated:** 2026-04-27
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
- **Current operating window:** 2026-04-20 through 2026-06-26
- **Status publication rule:** update this dashboard at least once per week; release-governance decisions reference this file plus `provider-validation-matrix.md`

---

## Subsystem Readiness Board

| Subsystem | Wave | Owner | Parity | Explainability | Calibration | Operator Sign-off | Kernel Readiness | Next Milestone (Target Date) | Evidence / Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Data quality + provider trust | DK1 | Data Operations & Provider Reliability owner | ✅ | 🟡 | 🟡 | ⚪ | At Risk | Complete operator review and sign-off for the Alpaca/Robinhood/Yahoo pilot packet (2026-05-01) | Evidence pack: [DK1 pilot parity runbook](./dk1-pilot-parity-runbook.md), [trust rationale mapping](./dk1-trust-rationale-mapping.md), [baseline thresholds + FP/FN review](./dk1-baseline-trust-thresholds.md), [provider validation matrix](./provider-validation-matrix.md), and a freshly generated `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json` attached for the review run. Generated provider-validation packets are no longer retained in git, so the removed `codex-dk1-packet-validation-final` path must not be treated as current evidence. The trading readiness lane exposes sample/evidence rows plus validated explainability and calibration contract status, and blocks legacy packets that omit those contracts. The packet generator accepts `-OperatorSignoffPath` so approved owner evidence is machine-readable, and the sign-off helper accepts `-PacketPath` so templates carry a `packetReview` binding to the reviewed packet path, generated timestamp, status, sample/evidence counts, and explainability/calibration contract status. DK1 remains open until a fresh packet, actual owner sign-off file, and owner review are complete. |
| Promotion + paper-trading cockpit | DK1 -> DK2 handoff | Trading Workstation owner | ⚪ | 🟡 | ⚪ | ⚪ | Early In Progress | Lock promotion-path audit fields + pilot review checklist (2026-05-08) | Depends on DK1 trust explainability and calibrated thresholds. Current source work persists a required `approvalChecklist` on promotion approvals alongside rationale, operator, source/target run type, lineage, manual override, and audit reference, and `Dk1TrustGateReadinessService` projects generated DK1 packet status plus pending sign-off through `TradingTrustGateReadinessDto` and a `ProviderTrustGate` operator work item. `GET /api/workstation/operator/inbox` now exposes those stable readiness work items alongside open or in-review reconciliation breaks with workspace/page/route navigation hints, giving W2-C a shared queue endpoint before WPF badge and notification-center consumption. The pilot audit sample is covered by `ExecutionWriteEndpointsTests.Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable`; DK2 entry/exit gates remain open. |
| Export + packaging | DK2 | Data Operations Export owner | 🟡 | 🟡 | ⚪ | ⚪ | Early In Progress | Validate governed report-pack schema v1 against pilot export scenarios (2026-05-15) | Governed report packs now carry `contractName=governance-report-pack` and `schemaVersion=1` across manifests, provenance, and artifact metadata; generation requests can pin `expectedSchemaVersion`, and incompatible future manifests are skipped by the repository. Remaining DK2 work is pilot parity across promoted run, export package, and reconciliation outputs. |
| Reconciliation + governance | DK2 | Governance/Fund Ops owner | 🟡 | 🟡 | ⚪ | ⚪ | Early In Progress | Approve reconciliation tolerance profile + exception playbook (2026-05-22) | `FileReconciliationBreakQueueRepository` now persists `reconciliation-break-queue.json` plus JSONL audit history via `AtomicFileWriter`; `/api/workstation/reconciliation/break-queue`, `/review`, `/resolve`, and `/audit` routes support seeded run-scoped breaks, assignment, resolve/dismiss, and audit history. Remaining DK2 work is calibrated tolerance/severity routing, external account/custodian parity, durable generalized governance casework, and operator sign-off. |
| Shared run/portfolio/ledger interop contracts | Cross-wave (DK1 + DK2) | Shared Platform Interop owner (Architecture + Contracts) | 🟡 | 🟡 | 🟡 | ⚪ | At Risk | Publish compatibility matrix + contract-change review cadence (2026-04-29) | Evidence: [contract compatibility matrix](./contract-compatibility-matrix.md) and `scripts/check_contract_compatibility_gate.py`. The gate now covers public removals, shared `UiApiRoutes` route constant removals/value changes, scoped record constructor parameter removals, and enum-member removals; owner sign-off on the review cadence remains open. |

---

## Current Implementation Commitments (Wave-Aligned)

| Window | Subsystem | Implemented commitment | Acceptance artifact |
| --- | --- | --- | --- |
| 2026-04-20 -> 2026-05-01 | Data quality + provider trust (DK1) | Standardize parity-runbook structure, replay/sample set, generated operator-review packet, and packet-bound sign-off preflight for DK1 pilot scope | Updated Wave 1 validation script output with `pilotReplaySampleSet`, `dk1-pilot-parity-packet.json/.md`, validated evidence-document checks, `prepare-dk1-operator-signoff.ps1 -PacketPath` validation coverage, and dashboard evidence links; operator sign-off remains pending |
| 2026-04-20 -> 2026-05-01 | Shared interop contracts (cross-wave) | Establish interop contract board, versioning policy, and compatibility matrix template | Contract compatibility matrix committed and linked from dashboard; gate expanded to catch scoped record-parameter and enum-member removals |
| 2026-05-02 -> 2026-05-15 | Promotion + paper cockpit (DK1/DK2) | Implement promotion rationale fields and operator approval checklist coverage | Source contract now requires `approvalChecklist`; approval audit metadata includes source/target run type and audit reference; acceptance artifact is `ExecutionWriteEndpointsTests.Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable` |
| 2026-05-09 -> 2026-05-22 | Export + packaging (DK2) | Freeze governed report-pack schema v1 and add version-compatibility checks | Acceptance artifacts: `FundOperationsWorkspaceReadServiceTests.GenerateReportPackAsync_WithDefaultFormats_WritesManifestProvenanceArtifactsAndChecksums`, `FundOperationsWorkspaceReadServiceTests.GenerateReportPackAsync_WithUnsupportedExpectedSchemaVersion_ThrowsArgumentException`, `FundOperationsWorkspaceReadServiceTests.ReportPackRepository_WithFutureSchemaVersion_SkipsManifest`, and `FundStructureEndpointTests.GenerateReportPack_WithSeededFundProfile_ReturnsPersistedSnapshot`; pilot parity report remains pending |
| 2026-05-16 -> 2026-05-29 | Reconciliation + governance (DK2) | Extend the file-backed reconciliation break queue into calibrated tolerance profiles, exception severity routing, and governance sign-off evidence | Current acceptance artifacts: `WorkstationEndpointsTests.MapWorkstationEndpoints_BreakQueueRoute_ShouldHydrateQueueWithoutGovernanceBootstrap`, `MapWorkstationEndpoints_BreakQueueReviewRoute_ShouldHydrateQueueWithoutListBootstrap`, and `MapWorkstationEndpoints_BreakQueueResolveRoute_ShouldRequireReviewBeforeResolve`; reconciliation exception burn-down and tolerance calibration summary remain pending |

---

## Entry/Exit Gate Checklist by Wave

## DK1 - Data quality + provider trust

### Entry checklist

- [x] **Parity entry:** Wave 1 closure evidence remains current and reproducible ([DK1 pilot parity runbook](./dk1-pilot-parity-runbook.md)).
- [ ] **Explainability entry:** trust signals have operator-visible source and rationale ([trust rationale mapping](./dk1-trust-rationale-mapping.md)).
- [ ] **Calibration entry:** baseline trust thresholds are declared for pilot operations ([baseline thresholds + FP/FN review](./dk1-baseline-trust-thresholds.md)).
- [ ] **Operator entry:** Data Ops + Trading approve DK1 pilot scope.

### Exit checklist

- [ ] **Parity pass:** cockpit views match validated provider/replay results for pilot scenarios ([DK1 pilot parity runbook](./dk1-pilot-parity-runbook.md)).
- [ ] **Explainability pass:** every trust alert maps to source, reason code, and operator action ([trust rationale mapping](./dk1-trust-rationale-mapping.md)).
- [ ] **Calibration pass:** thresholds tuned with documented false-positive/false-negative review ([baseline thresholds + FP/FN review](./dk1-baseline-trust-thresholds.md)).
- [ ] **Operator sign-off:** Data Ops + Trading owners approve DK1 completion.

## DK2 - Promotion + export + reconciliation

### Entry checklist

- [ ] **Parity entry:** DK1 is signed and shared contract seams are active.
- [ ] **Explainability entry:** promotion/export flows emit audit rationale.
- [ ] **Calibration entry:** reconciliation tolerances and severities are defined.
- [ ] **Operator entry:** Trading + Governance approve DK2 pilot playbook.

### Exit checklist

- [ ] **Parity pass:** promoted run, export, and reconciliation outputs agree across workstation/API/governance views.
- [ ] **Explainability pass:** end-to-end trace from trusted input to governed output is operator-visible.
- [ ] **Calibration pass:** reconciliation exceptions are tuned with no unresolved critical mismatches.
- [ ] **Operator sign-off:** Trading + Governance owners sign DK2 readiness.


#### Governance/fund-ops scenario gate text (mirrors Wave 4 objective pass/fail)

| Criterion | Required endpoint(s) + response fields | Required workstation surface behavior | Fail condition |
| --- | --- | --- | --- |
| Security Master conflict lifecycle is traceable end-to-end | `/api/security-master/conflicts`, `/api/security-master/conflicts/{conflictId}`, and `/api/security-master/conflicts/{conflictId}/resolve` must expose `ConflictReasonCode`, source-provenance identifiers, and resolution payload rationale (`ResolutionDecision`, `ResolutionRationale`, `Actor`, `TimestampUtc`, `CorrelationId`). | Operator can **search -> drill-in -> history -> resolution** for one conflicted instrument and see conflict reasons, source provenance, prior resolution history, and final resolution decision in one continuous flow. | Any missing linkage between conflict list/detail/resolution views, missing conflict reason code, missing source provenance, or missing explicit resolution rationale/audit chain in the same scenario run. |
| Corporate action provenance and parameter versioning remain explainable | `/api/security-master/corporate-actions` and `/api/security-master/trading-parameters` must return event provenance (`CorporateActionSource`, `IngestedAtUtc`) plus effective version fields (`EffectiveVersion`, `EffectiveFromUtc`, `SupersedesVersion`). | Operator can **search -> drill-in -> history -> resolution** from instrument view into corporate-action timeline and trading-parameter history, then resolve a flagged discrepancy with the effective-version trail visible. | Corporate-action timeline lacks provenance, trading-parameter change lacks effective-version traceability, or discrepancy resolution is recorded without explainable source/version linkage. |
| Governance audit trail is complete across fund-ops decisions | Governance workflow endpoints (`/api/fund-structure/workspace-view`, `/api/fund-structure/report-pack-preview`, and reconciliation decision endpoints) must emit audit metadata (`AuditActor`, `AuditTimestampUtc`, `CorrelationId`) and decision rationale fields for approvals/rejections. | Operator can **search -> drill-in -> history -> resolution** for an account/entity decision, inspect prior decision history, and complete or reject resolution with rationale that remains visible in history and governed output previews. | Any governance decision path that omits actor/timestamp/correlation, fails to retain decision rationale, or breaks history-to-resolution linkage between workspace and governed-output views. |

---

## Risk Register and Rollback Tracker

| Subsystem | Active Risk | Indicator | Current Mitigation | Rollback Trigger | Rollback Plan |
| --- | --- | --- | --- | --- | --- |
| Data quality + provider trust | validation/script and operator UI trust drift | unresolved trust alert deltas across script vs UI outputs | weekly matrix/script/doc sync check | two consecutive unresolved drift reports | pin last verified matrix + replay baseline, pause promotion expansion, rerun DK1 calibration |
| Promotion + paper cockpit | inconsistent approval state across UI/API | audit mismatch in promotion chain | shared promotion state schema review | any critical promotion audit mismatch | roll back to last signed promotion workflow contract, disable new lanes by feature flag |
| Export + packaging | schema/version drift in governed exports | export validation failures or missing lineage | contract version freeze for pilot | repeated export validation failures on signed scenarios | revert exporter version, regenerate from last good run snapshots |
| Reconciliation + governance | tolerance miscalibration causing false flood or miss | sustained unresolved critical exception count | staged tolerance tuning with governance review | unresolved critical exception threshold breach | restore prior tolerance profile, reprocess affected window, require manual approvals |
| Shared interop contracts | incompatible DTO/API changes between subsystems | cross-workspace contract test failures | compatibility matrix + contract review board | any contract-breaking change without approved migration | revert shared contract version/API shape and block downstream deploys |

---

## Kernel Observability Controls

- **Dashboard surfaces:** Data Operations and Governance now expose per-domain kernel latency `p50/p95/p99`, throughput per minute, reason-code coverage, determinism mismatches, score drift, severity drift, and active versus historical critical-jump alerts.
- **Determinism evidence:** non-production and diagnostic paths compare stable input/output hashes so the same routed request can be checked for unexpected output divergence without impacting production execution paths.
- **Drift methodology:** score and severity drift are calculated as total variation distance between recent and trailing windows so operators can see real distribution shift, not only average movement.
- **Critical-jump alert thresholds:** headline cards show active alerts while payloads retain total alert count; the current activation policy requires at least 20 samples in both windows, a short-window critical rate of at least 25%, a zero-baseline trigger of 35%, or a 2.0x relative jump with at least a 0.15 absolute increase.

---

## Alignment Notes (Waves 2-4)

- **Wave 2 (cockpit hardening):** execution tracked through DK1 trust and explainability gates.
- **Wave 3 (shared-model continuity):** split between DK1 trust dependencies and DK2 promotion/export/reconciliation continuity.
- **Wave 4 (governance productization):** readiness claim requires DK2 exit and Governance operator sign-off.

This dashboard is the single status surface for DK program tracking. Use it with [`ROADMAP.md`](ROADMAP.md) and [`ROADMAP_COMBINED.md`](ROADMAP_COMBINED.md) to avoid duplicate or parallel migration plans.
