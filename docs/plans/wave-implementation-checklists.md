# Meridian Wave Implementation Checklists

**Last Reviewed:** 2026-05-19  
**Purpose:** Plain-language, implementation-ready checklists for what must be finished before each wave can be marked complete.

Use this together with:

- `docs/plans/current-direction-and-status.md`
- `docs/status/ROADMAP.md`
- `docs/status/PROGRAM_STATE.md`

---

## How To Use This Checklist

- Treat every parent checkbox as required for wave completion.
- Use the sub-items as the concrete implementation/validation steps.
- If a parent item is not complete, add the blocker to the matching pilot-readiness stage gate.
- Do not mark a wave complete because a feature exists; mark complete only after acceptance evidence exists.

---

## Wave 1 — Provider Confidence And Checkpoint Evidence (Done, Maintenance)

### Completion Checklist

- [ ] **DK1 validation packet is current when provider evidence changes.**
  - [ ] Regenerate the DK1 packet after any provider feed, schema, or reliability evidence change.
  - [ ] Confirm packet timestamps and source inputs match the latest validation run.
  - [ ] Store the updated packet in the expected provider-validation artifact path.

- [ ] **Provider validation matrix reflects current results.**
  - [ ] Update `docs/status/provider-validation-matrix.md` with latest pass/fail posture.
  - [ ] Verify provider statuses match the latest validation artifacts.
  - [ ] Record any changed provider risk notes or operational constraints.

- [ ] **Kernel readiness and contract compatibility dashboards are synchronized.**
  - [ ] Confirm readiness dashboard status matches the latest trust-gate results.
  - [ ] Confirm contract compatibility entries are aligned with current provider contracts.
  - [ ] Resolve or document any mismatch between dashboards before closing the item.

- [ ] **Calibration and sign-off artifacts are traceable.**
  - [ ] Save calibration inputs/outputs in a stable artifact location.
  - [ ] Save operator sign-off output with date, owner, and linked packet.
  - [ ] Ensure a reviewer can find all W1 artifacts without implicit tribal knowledge.

- [ ] **Trust-gate regressions have ownership and recovery timelines.**
  - [ ] Log each regression with a clear impact statement.
  - [ ] Assign a directly responsible owner.
  - [ ] Add a target fix date and near-term mitigation plan.

---

## Wave 2 — Paper-Trading Cockpit Reliability (In Progress)

### Completion Checklist

- [ ] **Trading readiness endpoint reports accurate state.**
  - [ ] Validate `GET /api/workstation/trading/readiness` for expected readiness fields.
  - [ ] Confirm the endpoint reflects current trust, replay, and promotion posture.
  - [ ] Verify no stale readiness snapshots are presented after state changes.

- [ ] **Operator inbox endpoint routes the right action items.**
  - [ ] Validate `GET /api/workstation/operator/inbox` includes readiness and reconciliation actions.
  - [ ] Confirm routing hints/deep links map to the correct workflow destinations.
  - [ ] Verify account-scoped scenarios show the correct scoped work items.

- [ ] **Paper-session replay verification is fresh and durable.**
  - [ ] Run replay verification for active paper sessions.
  - [ ] Confirm replay evidence survives restart and can be reloaded.
  - [ ] Ensure stale replay evidence is detected and re-verification is required.

- [ ] **Promotion gates enforce trust and control requirements.**
  - [ ] Validate promotion checklist blocks advancement when required evidence is missing.
  - [ ] Verify gating reasons are human-readable for operators.
  - [ ] Confirm gate state changes are auditable and linked to evidence.

- [ ] **Browser trading cockpit is operator-accepted.**
  - [ ] Run operator-focused scenario validation for key cockpit workflows.
  - [ ] Capture acceptance evidence for reliability and usability expectations.
  - [ ] Record unresolved operator concerns as blockers instead of soft notes.

- [ ] **W2 pilot-readiness gates are green or explicitly blocked.**
  - [ ] Check `TrustedData`, `PaperPromotion`, and `PaperSession` gate states.
  - [ ] For any non-green gate, add explicit blocker text and owner.
  - [ ] Link blockers to concrete follow-up tasks and evidence expectations.

---

## Wave 3 — Shared Run / Portfolio / Ledger Continuity (In Progress)

### Completion Checklist

- [ ] **Research runs and comparisons are consistent across workflows.**
  - [ ] Verify run data shown in research and downstream views is consistent.
  - [ ] Validate comparison outputs are reproducible for the same inputs.
  - [ ] Document and resolve any cross-workflow drift.

- [ ] **Portfolio and ledger stay aligned with run/promotion state.**
  - [ ] Confirm portfolio summaries reflect current approved run outcomes.
  - [ ] Confirm ledger entries and counts align with portfolio-facing state.
  - [ ] Verify state transitions do not create orphaned or contradictory records.

- [ ] **Brokerage/account synchronization is end-to-end reliable.**
  - [ ] Validate account sync from source ingestion through surfaced read models.
  - [ ] Confirm sync history and readiness signals reflect true sync posture.
  - [ ] Ensure failures surface actionable remediation steps.

- [ ] **Reconciliation handoffs preserve context and evidence.**
  - [ ] Verify reconciliation items include required evidence references.
  - [ ] Confirm operator context (who/what/why/next step) is preserved in handoffs.
  - [ ] Validate sign-off outcomes are auditable.

- [ ] **Shared contracts/read models work across browser and retained desktop.**
  - [ ] Validate shared DTO/read-model compatibility in both UI surfaces.
  - [ ] Confirm no wave-critical behavior diverges between browser and retained desktop.
  - [ ] Record compatibility exceptions and mitigation plans if any are unavoidable.

- [ ] **W3 pilot-readiness gates are green or explicitly blocked.**
  - [ ] Check `TrustedData`, `ResearchRun`, `RunComparison`, `PaperPromotion`, `PortfolioLedgerReview`, and `Reconciliation`.
  - [ ] For any non-green gate, document blocker, owner, and due date.
  - [ ] Attach evidence links showing current state and recovery plan.

---

## Wave 4 — Governance And Fund Operations (In Progress)

### Completion Checklist

- [ ] **Reconciliation casework is durable and operationally usable.**
  - [ ] Validate queue lifecycle, assignment, and state transitions.
  - [ ] Confirm case history/audit records are complete and queryable.
  - [ ] Ensure casework survives restart and supports resumed operations.

- [ ] **Approval/sign-off workflows include clear provenance.**
  - [ ] Confirm each approval records actor, timestamp, decision, and rationale.
  - [ ] Verify rejected/returned paths are captured and recoverable.
  - [ ] Ensure provenance data is linked to related evidence and artifacts.

- [ ] **Governed report-pack lifecycle is complete.**
  - [ ] Validate build, validation, versioning, and publish steps end-to-end.
  - [ ] Confirm report-pack outputs include required metadata and evidence links.
  - [ ] Ensure lifecycle failures are visible with actionable recovery guidance.

- [ ] **Evidence chains connect operations to final outputs.**
  - [ ] Verify trace links from trusted data -> run -> ledger -> reconciliation -> report.
  - [ ] Confirm references are stable and can be re-opened during audit/review.
  - [ ] Close any broken or ambiguous evidence links before completion.

- [ ] **Security Master and accounting controls support close/report operations.**
  - [ ] Validate required security and accounting control checks for close workflows.
  - [ ] Confirm control outcomes are captured in operator-visible status.
  - [ ] Ensure exception handling is documented and governed.

- [ ] **W4 pilot-readiness gates are green or explicitly blocked.**
  - [ ] Check `TrustedData`, `PortfolioLedgerReview`, `Reconciliation`, and `GovernedReportPack`.
  - [ ] For any non-green gate, document blocker, owner, and resolution target.
  - [ ] Attach evidence references for both current posture and remediation.

---

## Wave 5 — Backtest Studio Unification (Planned)

### Completion Checklist

- [ ] **W2-W4 critical blockers are materially closed first.**
  - [ ] Confirm W2-W4 are no longer blocked by speculative/open core dependencies.
  - [ ] Record any remaining dependencies with approved defer rationale.

- [ ] **Backtest workflows align to shared contracts.**
  - [ ] Ensure backtest flows use shared evidence and promotion contracts.
  - [ ] Validate output consistency with portfolio/ledger/reports expectations.

- [ ] **Studio outputs integrate into core operations flows.**
  - [ ] Confirm outputs feed portfolio and ledger workflows without custom side paths.
  - [ ] Confirm governed reporting can consume studio artifacts where required.

- [ ] **Operator acceptance criteria are defined and verified.**
  - [ ] Document acceptance criteria before claiming completion.
  - [ ] Capture verification evidence that criteria are met.

---

## Wave 6 — Live Integration Readiness (Planned)

### Completion Checklist

- [ ] **Read-only/paper-first safeguards are relaxed with explicit governance approval.**
  - [ ] Document approval decision, owner, and effective scope.
  - [ ] Confirm safeguards are relaxed only where approved.

- [ ] **Live trust/safety controls and rollback paths are validated.**
  - [ ] Validate live controls under expected and failure conditions.
  - [ ] Verify rollback procedures are tested and time-bounded.

- [ ] **Runbooks support real operations recovery.**
  - [ ] Confirm runbooks cover incident response and reconciliation recovery.
  - [ ] Confirm reporting recovery and data integrity verification steps are included.

- [ ] **Production-readiness evidence is complete and reviewable.**
  - [ ] Gather evidence across data, execution, accounting, and governance.
  - [ ] Verify evidence can be reviewed end-to-end by operators/auditors.

---

## Cross-Wave Exit Criteria (Required For Every Wave)

- [ ] **Program state is updated.**
  - [ ] Update owner, status, and target date in `docs/status/PROGRAM_STATE.md`.
  - [ ] Verify updates match the latest accepted evidence.

- [ ] **Roadmap claims are updated conservatively.**
  - [ ] Update `docs/status/ROADMAP.md` with objective, evidence-backed wording.
  - [ ] Avoid claim language that implies closure without acceptance evidence.

- [ ] **Pilot-acceptance evidence is attached or linked.**
  - [ ] Link or attach `artifacts/pilot-acceptance/latest/*` outputs.
  - [ ] Confirm evidence links are accessible to reviewers.

- [ ] **Remaining blockers are explicit and assigned.**
  - [ ] List unresolved blockers with severity and owner.
  - [ ] Include next action and target date for each blocker.

- [ ] **Documentation/operator guidance ships in the same PR.**
  - [ ] Update affected docs and operator guidance for behavior changes.
  - [ ] Confirm reviewers can execute updated workflows from docs alone.
