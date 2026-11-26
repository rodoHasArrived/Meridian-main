# Governed Strategy, OMS, and Execution Implementation Plan

**Status:** draft
**Owner:** core-team
**Reviewed:** 2026-11-18
**Roadmap items:** `W13-STRAT-001`, `W13-OMS-001` (status remains in the [roadmap registry](../../roadmap/README.md))

This plan turns the 2026-11-17 brainstorm for strategy formulation, order management, and
governed strategy execution into phased, independently mergeable slices. It is a working input,
not a status source, and it grants no live activation authority. Every slice is validated with
synthetic or paper fixtures under the [solo-developer workflow](../../ai/solo-developer-workflow.md);
any live-support claim still needs separately authorized broker-backed evidence.

## Current-state anchors

These existing seams are extended; none are replaced wholesale.

| Concern | Existing seam | Gap this plan closes |
|---|---|---|
| Run reproducibility | `StrategyRunEntry.ComputeEvidenceBoundInputHash` / `ComputeRealismBoundInputHash` / `ComputeAccountBoundInputHash` | Hashes cover run inputs, but there is no single **strategy-artifact** identity spanning Designer, QuantScript, and backtest-adapted routes |
| Strategy authoring | `Live/Designer/DesignerDocumentRevision.cs`, `LiveStrategyCatalog`, `BacktestStrategyLiveAdapter` | No capability matrix per route; edits do not mark approvals stale |
| Validation | `StrategyEngineValidationService`, `StrategyDesignService` | Validation logic is split; browser/WPF can drift |
| Live authority | `ILiveOrderReadinessGate` → `TradingOperatorLiveOrderReadinessGate` (FundAccountId + promotion `TargetRunId` + readiness snapshot) | Authority is re-derived per order; there is no first-class, expiring, revocable authorization record |
| Promotion | `PromotionApprovalChecklist`, `IPromotionRecordStore`, `PromotionService` | Approval is not bound to a strategy-artifact hash |
| Strategy → OMS path | `LiveTradingEngine` → `IOrderGateway` → `OmsGovernedExecutionOrderGateway` | Path is governed by DI wiring only; no test asserts that a bypass gateway cannot be wired in for live mode |
| Admission | `OrderAdmissionPolicy` (internal) | No declarative broker/account capability matrix; unsupported combinations fail late |
| Amendments / cancel | `OrderAmendmentBook`, `OrderManagementSystem.PendingAmendments/Cancellation/KillSwitch` | Need explicit, test-proven "never terminal until confirmed" semantics and per-order cancel-all outcomes surfaced to UI |
| Lifecycle robustness | `OrderStateTable`, `OrderFillProcessor`, `FillIdentity`, `Recovery`, `BrokerOrderSweep` | Strong unit tests exist (`tests/Meridian.Tests/Execution/*`), but no shared fault-injection harness |
| Run continuity | `StrategyRunContinuityService`, `RestoredPausedLiveStrategy`, `LiveRunMetricsTracker`, `ReplayDriftDetector` | No mandate-drift guard; transitions do not retain open-order/exposure posture as one durable record |
| OMS size | 14+ `OrderManagementSystem.*.cs` partials, `OrderLifecycleManager`, `OrderManagementLifecycleCoordinator` | Ownership is unclear; tracked by the [god-file burn-down](god-file-burn-down-plan.md) |

## Phasing overview

```
Phase 0  Safety net             O3 fault-injection harness, E2 path-pinning test
Phase 1  Identity & authority   S1 strategy-artifact identity → E1 live authorization record
Phase 2  Admission & lifecycle  O1 capability matrix, O2 uncertain-state semantics, S4 approval invalidation
Phase 3  Runtime governance     E3 durable transitions, E4 mandate-drift guard, S2 version pinning
Phase 4  Surfaces & parity      S5 shared validator, O4 shared order-workbench read model, S3 route matrix
Phase 5  Proof & refactor       E5 end-to-end rehearsal, O5 OMS decomposition
```

Phase 0 comes first because it only adds tests, carries the least risk, and protects every later
phase. S1 and E1 are on the critical path; most later slices consume their contracts.

---

## Phase 0: Safety net (tests only, no behaviour change)

### O3: Order-lifecycle fault-injection harness

- **Add** `tests/Meridian.TestSupport/Execution/BrokerReportScript.cs`: a fluent builder that emits
  sequences of synthetic `ExecutionReport`s with injectable faults: duplicate, reordered,
  conflicting client-order ID, gap/reconnect, dropped ack, and crash-between-commit.
- **Add** `tests/Meridian.Tests/Execution/OrderLifecycleFaultInjectionTests.cs`, driving
  `OrderManagementSystem` + `PaperTradingGateway` + `FileBrokerageOrderRecoveryStore` (temp dir).
- **Invariants asserted** (property-style over a seeded generator):
  1. Lifecycle state is monotonic per order (`OrderStateTable`).
  2. Cumulative filled quantity never exceeds order quantity, and each fill ID is applied exactly once.
  3. No order is dispatched twice across a simulated restart (`OrderDispatchRegistry`, recovery journal).
  4. Accounting handoff count equals the unique fill count (`AccountingHandoffCoordinator`).
- **Validation:** `dotnet test tests/Meridian.Execution.Tests --filter FullyQualifiedName~FaultInjection`.
- **Effort:** M. **Risk:** none (tests only). Any invariant failure becomes its own bug-fix slice.

### E2: Pin the single governed order path

- **Add** a host-composition test (`tests/Meridian.Tests/Execution/LiveEngineGatewayCompositionTests.cs`)
  that builds the service provider as `UiServer`/`AddLiveTradingEngine` does and asserts that the
  `IOrderGateway` resolved for `LiveTradingEngine` is `OmsGovernedExecutionOrderGateway` in
  paper and live modes.
- **Add** a guard: in live mode, `LiveTradingEngine` construction fails closed if the injected
  gateway is not OMS-governed (marker interface `IOmsGovernedOrderGateway` in `Meridian.Execution.Sdk`).
- **Effort:** S. **Risk:** low. The guard only rejects a misconfiguration.

---

## Phase 1: Identity and authority (critical path)

### S1: Versioned strategy-artifact identity

- **Contract** (`Meridian.Contracts/Strategies/StrategyArtifactIdentity.cs`):
  `StrategyArtifactId` (stable logical ID) + `ArtifactRevision` (monotonic) + `ArtifactHash`
  (SHA-256 over canonical JSON of authoring route, source/expression, parameters, universe,
  cost assumptions, and risk limits). Use the source-generated JSON context; reuse the canonical
  appender style from `StrategyRunEntry`.
- **Producers:** Designer (`DesignerDocumentRevision`), QuantScript, and backtest-adapted
  strategies each implement `IStrategyArtifactSource.Describe()` → canonical payload.
- **Storage:** append-only `StrategyArtifactStore` (WAL/atomic-write pattern, like existing stores).
- **Binding:** `StrategyRunEntry` gains optional `ArtifactId`/`ArtifactHash`. Legacy runs stay
  readable with `ArtifactHash = null` → shown as *unbound*, never inferred.
- **Tests:** hash stability (same input → same hash across processes), order-insensitivity of
  sets, sensitivity to every governed field, legacy-run deserialization.
- **Effort:** L. **Risk:** medium (touches retained-run schema). Additive fields only.

### E1: First-class live authorization record

- **Contract** (`Meridian.Execution.Sdk/LiveAuthorization.cs`):
  `LiveAuthorization { AuthorizationId, FundAccountId, PromotedRunId, ArtifactHash,
  PromotionRecordId, ReadinessSnapshotRef, Mandate (instruments, max notional, max gross, order types),
  IssuedAt, ExpiresAt, RevokedAt?, RevocationReason? }`.
- **Service:** `LiveAuthorizationService` (issue / revoke / get-current), append-only durable store.
  Issuing requires a complete `PromotionApprovalChecklist` for `RunType.Live` and a matching
  `ArtifactHash` (from S1).
- **Gate change:** `TradingOperatorLiveOrderReadinessGate.EvaluateAsync` additionally resolves the
  current authorization for `(FundAccountId, RunId)` and rejects with explicit reasons:
  `authorization_missing`, `authorization_expired`, `authorization_revoked`,
  `artifact_hash_mismatch`, or `outside_mandate`. Existing checks are kept; the new ones are
  additive and fail closed.
- **Revocation effect:** revoke → new submissions refused immediately; open orders are left for an
  explicit operator cancel decision (no implicit mass cancel).
- **Tests:** each rejection reason; a race test showing that a revoke concurrent with a submit never
  dispatches after revoke commits; restart preserves revocation.
- **Effort:** L. **Risk:** high (execution path). Needs a separate AI critique pass per the solo workflow.

---

## Phase 2: Admission and lifecycle correctness

### O1: Broker/account capability matrix

- **Contract:** `ExecutionCapabilityMatrix` (instrument class × order type × TIF × mode) declared
  per broker adapter and per account; config overlays may *narrow* but never widen.
- **Enforcement:** `OrderAdmissionPolicy` checks the matrix before risk; a refusal carries
  `capability_unsupported:{dimension}`. `OrderPreviewService` surfaces the same reason pre-submit.
- **Evidence flag:** each matrix cell carries `EvidenceLevel = Declared | PaperTested | LiveAccepted`;
  UI shows anything below `LiveAccepted` as *not live-certified*.
- **Effort:** M. **Risk:** medium. Start with paper adapters, then Alpaca.

### O2: Uncertain-state semantics for amend/cancel/cancel-all

- Introduce explicit `PendingCancel`, `PendingReplace`, and `Indeterminate` display states derived
  from `OrderAmendmentBook` + broker observations; terminal states only on broker confirmation.
- Cancel-all returns `CancelAllOutcome { perOrder: [Confirmed | Rejected | Pending | Indeterminate], residualExposure }`
  and persists it to the execution journal.
- **Tests:** extend the O3 harness with dropped cancel acks, replace-reject, and fill-during-cancel.
- **Effort:** M. **Risk:** medium.

### S4: Revision diff and approval invalidation

- `StrategyArtifactDiff` classifies changes as `Cosmetic | Parameter | Universe | Logic | RiskLimit`.
  Any non-cosmetic change produces a new `ArtifactHash`, so the E1 gate rejects with
  `artifact_hash_mismatch` until a new promotion is made.
- UI: diff panel on the Strategy workspace plus a "requires re-validation" badge.
- **Effort:** M. **Risk:** low (consumes S1/E1).

---

## Phase 3: Runtime governance

### E3: Durable run transitions with posture snapshots

- `StrategyRunTransition { RunId, From, To, Intent, OpenOrders[], NetExposure, CancellationOutcomes, Actor, CorrelationId }`
  written atomically **before** side effects (intent) and completed after (outcome).
- Resume path (`RestoredPausedLiveStrategy`, `StrategyRunContinuityService`) must run broker
  reconciliation (`BrokerOrderSweep`) before re-arming; an unreconciled state stays `Paused:Unreconciled`.
- **Tests:** crash between intent and outcome; concurrent pause+stop; a resume with a broker-side fill
  that occurred while paused.
- **Effort:** L. **Risk:** high.

### E4: Mandate-drift guard

- `MandateDriftMonitor` subscribes to `LiveRunMetricsTracker` and compares the result with the E1 mandate
  (instrument set, notional, turnover, drawdown). Breaches are classed as **soft** (alert) or **hard**
  (auto-pause via E3 plus a `GovernanceExceptionService` case with evidence).
- Thresholds live in Options with validation; no hard-coded limits.
- **Effort:** M. **Risk:** medium.

### S2: Data/engine version pinning on run requests

- Run requests carry `DataSnapshotRef`, `EngineVersion`, and `ArtifactHash`; the orchestrator rejects
  stale or ambiguous inputs with a `Blocked | Degraded | Deferred` status and a reason. Results are
  never recorded without pins.
- **Effort:** M. **Risk:** low to medium.

---

## Phase 4: Surfaces and parity

| Slice | Work | Browser | WPF |
|---|---|---|---|
| S5 | Merge `StrategyEngineValidationService`/`StrategyDesignService` checks into one `StrategyValidationService` behind a Ui.Shared endpoint | consume endpoint | consume same service |
| O4 | `OrderWorkbenchReadModel` in `Meridian.Ui.Services`: tickets, working orders, fills, recovery, risk decisions, O2 states, and disabled-control reasons | Trading workspace | `W8-WPF-PARITY-001` parity |
| S3 | Route capability matrix (Designer / QuantScript / backtest-adapted × backtest/paper/live). Unsupported constructs fail validation with the route named | Strategy workspace | same |

Navigation stays within the seven top-level workspaces (Strategy, Trading). Keep docs aligned in
`docs/engineering/plans/wpf-web-ui-alignment-plan.md`.

---

## Phase 5: Proof and refactor

### E5: End-to-end governed rehearsal

A scripted, synthetic-only test (`tests/Meridian.Tests/Integration/GovernedStrategyRehearsalTests.cs`)
covers: Designer strategy → artifact hash (S1) → backtest with pins (S2) → paper run → promotion
checklist → live authorization (E1) against the **paper** gateway flagged as live-mode simulation →
fills → reconciliation → accounting handoff. It asserts lineage end to end. It must also prove that
paper evidence is labelled *paper* and cannot satisfy `LiveAccepted`.

### O5: OMS decomposition

Follow the [god-file burn-down](god-file-burn-down-plan.md): extract `RecoveryCoordinator`,
`KillSwitchController`, and `RetentionService` from the OMS partials behind internal interfaces,
one extraction per PR, with the O3 harness as the regression net. No behaviour change.

---

## Sequencing and dependencies

| # | Slice | Depends on | Size | Risk |
|---|---|---|---|---|
| 1 | O3 fault-injection harness | n/a | M | none |
| 2 | E2 governed-path pinning | n/a | S | low |
| 3 | S1 strategy-artifact identity | n/a | L | med |
| 4 | E1 live authorization record | S1 | L | high |
| 5 | O1 capability matrix | n/a | M | med |
| 6 | O2 uncertain-state semantics | O3 | M | med |
| 7 | S4 approval invalidation | S1, E1 | M | low |
| 8 | E3 durable transitions | O3, E1 | L | high |
| 9 | E4 mandate-drift guard | E1, E3 | M | med |
| 10 | S2 version pinning | S1 | M | low–med |
| 11 | S5 / O4 / S3 surfaces | S1, O1, O2 | M each | low |
| 12 | E5 rehearsal | all of the above | M | low |
| 13 | O5 OMS decomposition | O3 | M (rolling) | low per PR |

Slices 1, 2, 3, and 5 can run as parallel lanes. If you do, initialize
`docs/ai/parallel-task-manifest-template.md`.

## Implementation checklist

- [ ] O3: lifecycle fault-injection harness and invariants
- [ ] E2: governed order-path composition test and live-mode guard
- [ ] S1: strategy-artifact identity, store, and run binding
- [ ] E1: live authorization record, service, and gate rejections
- [ ] O1: broker/account capability matrix in admission and preview
- [ ] O2: pending/indeterminate amend and cancel semantics, plus cancel-all outcomes
- [ ] S4: revision diff and approval invalidation
- [ ] E3: durable run transitions with posture snapshots and reconcile-before-resume
- [ ] E4: mandate-drift guard with soft/hard breach handling
- [ ] S2: data/engine version pinning on run requests
- [ ] S5 / O4 / S3: shared validator, order-workbench read model, route capability matrix (browser and WPF)
- [ ] E5: end-to-end governed rehearsal test
- [ ] O5: OMS partial-class decomposition (rolling)

## Validation per slice

- Targeted tests only:
  `dotnet test tests/Meridian.Execution.Tests --filter FullyQualifiedName~<Area>` (or the
  `Targeted Test` workflow with `mode=dotnet-filtered` when local runs are unreliable).
- UI slices: `npm --prefix src/Meridian.Ui/dashboard run test` plus `Meridian.Wpf.Tests` filters.
- High-risk slices (E1, E3, O2) get a separate AI critique pass for execution/risk, storage, and
  accounting before merge.
- `Meridian CI / quality-gate` stays the authoritative merge gate.

## Out of scope / guardrails

- No live broker activation, credential changes, or release certification.
- No mobile surfaces.
- Model-generated suggestions never create or extend a `LiveAuthorization`.
- Arbitrary-code (QuantScript) live execution stays deferred until PRD-012 isolation lands.

## Open decisions

1. Default `LiveAuthorization` lifetime (proposal: 1 trading day, renewable) and who may renew
   in the single-operator envelope (see `W14-ATTEST-001`).
2. Whether S1 hashes cosmetic metadata such as names and descriptions (proposal: exclude it).
3. Whether a hard mandate breach also cancels open orders or only pauses (proposal: pause only;
   cancel stays an explicit operator action).
