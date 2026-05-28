# Meridian Wave Implementation Checklists

**Last Reviewed:** 2026-05-27

## TODO Checklist (Concrete Implementation Items)
- [ ] Define scope boundaries for **wave implementation checklists** and document explicit in-scope vs out-of-scope items.
- [ ] Break delivery into PR-sized milestones with owner, dependency, and evidence artifact for each milestone.
- [ ] Implement the first milestone in code/config/scripts and link the exact validating test or command output.
- [ ] Add/update operator runbook steps and rollback procedure for the wave implementation checklists workflow.
- [ ] Record completion evidence in `docs/status/` (or linked packet) and mark corresponding checklist items done.

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

### Desktop workflow acceptance filter for Waves 2-4

For retained desktop/WPF work, use
`docs/plans/desktop-ui-workflow-acceptance-matrix.md` before counting any slice as Wave 2, Wave 3,
or Wave 4 progress.

- Lane A maps desktop work to Wave 2 trading cockpit reliability.
- Lane B maps desktop work to Wave 3 run -> portfolio -> ledger continuity.
- Lane C maps desktop work to Wave 4 reconciliation/governance close flow.
- Every desktop scenario must name the shared contract or endpoint, the focused WPF evidence, the
  browser parity check or explicit drift blocker, and the matching pilot-readiness stage posture.
- WPF may compose and present shared workflow state, but it must not become the only source of
  wave-critical business behavior.

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

## Wave 2 — Paper-Trading Cockpit Reliability (Done)

### Completion Checklist

- [x] **Desktop Wave 2 work closes Lane A scenarios from the desktop acceptance matrix.**
  - [x] Prove the happy path for paper-session restore, replay verification, readiness review, and
        next-action routing through shared readiness and operator-inbox contracts.
  - [x] Prove blocker-path behavior for stale replay, execution-control, promotion-review,
        brokerage-sync, or related shared work items without relying on desktop-only wording or
        routing rules.
  - [x] Prove recovery-path refresh behavior so WPF and browser both clear or downgrade blocked
        state from the latest shared payload after remediation.

- [x] **Synthetic harness enablement baseline is complete before Wave A provider completion.**
  - [x] Deterministic fixtures are versioned and used by the acceptance harness.
  - [x] Replay utilities are available for repeatable restore/replay verification flows.
  - [x] Fault-injection hooks exist for provider-path resilience scenarios in the same harness lane.

- [x] **Trading readiness endpoint reports accurate state.**
  - [x] Validate `GET /api/workstation/trading/readiness` for expected readiness fields.
  - [x] Confirm the endpoint reflects current trust, replay, and promotion posture.
  - [x] Verify no stale readiness snapshots are presented after state changes.

- [x] **Operator inbox endpoint routes the right action items.**
  - [x] Validate `GET /api/workstation/operator/inbox` includes readiness and reconciliation actions.
  - [x] Confirm routing hints/deep links map to the correct workflow destinations.
  - [x] Verify account-scoped scenarios show the correct scoped work items.

- [x] **Paper-session replay verification is fresh and durable.**
  - [x] Run replay verification for active paper sessions.
  - [x] Confirm replay evidence survives restart and can be reloaded.
  - [x] Ensure stale replay evidence is detected and re-verification is required.

- [x] **Promotion gates enforce trust and control requirements.**
  - [x] Validate promotion checklist blocks advancement when required evidence is missing.
  - [x] Verify gating reasons are human-readable for operators.
  - [x] Confirm gate state changes are auditable and linked to evidence.

- [x] **Browser trading cockpit is operator-accepted.**
  - [x] Run operator-focused scenario validation for key cockpit workflows.
  - [x] Capture acceptance evidence for reliability and usability expectations.
  - [x] Record unresolved operator concerns as blockers instead of soft notes.

- [x] **W2 pilot-readiness gates are green or explicitly blocked.**
  - [x] Check `TrustedData`, `PaperPromotion`, and `PaperSession` gate states.
  - [x] For any non-green gate, add explicit blocker text and owner.
  - [x] Link blockers to concrete follow-up tasks and evidence expectations.

- [x] **Harness parity checks pass before broad provider wave rollout.**
  - [x] Run parity checks comparing deterministic harness outputs across the Wave A provider target set.
  - [x] Block broad rollout when parity mismatches are unresolved.
  - [x] Record parity evidence links and owners for any temporary exceptions.

Evidence: closed by the 2026-05-27 W2/W3 evidence slice in
`docs/plans/current-direction-and-status.md`; latest pilot readiness gates `TrustedData`,
`PaperPromotion`, and `PaperSession` are `Ready` with no blockers.

---

## Wave 3 — Shared Run / Portfolio / Ledger Continuity (Done)

### Completion Checklist

- [x] **Desktop Wave 3 work closes Lane B scenarios from the desktop acceptance matrix.**
  - [x] Prove the happy path from retained run/session -> portfolio -> ledger -> cash-flow ->
        reconciliation using shared run, portfolio, ledger, and continuity services.
  - [x] Prove blocker-path behavior for missing run context, stale brokerage/account sync,
        ledger/cash-flow mismatches, or reconciliation gaps without presenting a blank view as
        success.
  - [x] Prove recovery-path behavior where restored account sync, retained run context, filter
        reset, or reconciliation refresh makes the same shared evidence visible again in both UI
        lanes.

- [x] **Research runs and comparisons are consistent across workflows.**
  - [x] Verify run data shown in research and downstream views is consistent.
  - [x] Validate comparison outputs are reproducible for the same inputs.
  - [x] Document and resolve any cross-workflow drift.

- [x] **Portfolio and ledger stay aligned with run/promotion state.**
  - [x] Confirm portfolio summaries reflect current approved run outcomes.
  - [x] Confirm ledger entries and counts align with portfolio-facing state.
  - [x] Verify state transitions do not create orphaned or contradictory records.

- [x] **Brokerage/account synchronization is end-to-end reliable.**
  - [x] Validate account sync from source ingestion through surfaced read models.
  - [x] Confirm sync history and readiness signals reflect true sync posture.
  - [x] Ensure failures surface actionable remediation steps.

- [x] **Reconciliation handoffs preserve context and evidence.**
  - [x] Verify reconciliation items include required evidence references.
  - [x] Confirm operator context (who/what/why/next step) is preserved in handoffs.
  - [x] Validate sign-off outcomes are auditable.

- [x] **Shared contracts/read models work across browser and desktop.**
  - [x] Validate shared DTO/read-model compatibility in both UI surfaces.
  - [x] Confirm no wave-critical behavior diverges between browser and desktop.
  - [x] Record compatibility exceptions and mitigation plans if any are unavoidable.

- [x] **W3 pilot-readiness gates are green or explicitly blocked.**
  - [x] Check `TrustedData`, `ResearchRun`, `RunComparison`, `PaperPromotion`, `PortfolioLedgerReview`, and `Reconciliation`.
  - [x] For any non-green gate, document blocker, owner, and due date.
  - [x] Attach evidence links showing current state and recovery plan.

Evidence: closed by the 2026-05-27 W2/W3 evidence slice in
`docs/plans/current-direction-and-status.md`; latest pilot readiness gates `TrustedData`,
`ResearchRun`, `RunComparison`, `PaperPromotion`, `PortfolioLedgerReview`, and `Reconciliation`
are `Ready` with no blockers.

---

## Wave 4 — Governance And Fund Operations (In Progress)

### Completion Checklist

- [ ] **Desktop Wave 4 work closes Lane C scenarios from the desktop acceptance matrix.**
  - [ ] Prove the happy path for reconciliation case review, sign-off posture, close-lane
        readiness, and report-pack evidence from one governed context.
  - [ ] Prove blocker-path behavior for unresolved casework, missing tolerance/sign-off metadata,
        approval gaps, report-pack validation failures, or broken evidence chains with clear next
        actions.
  - [ ] Prove recovery-path behavior where case decisions, approvals/sign-off, or report-pack
        regeneration refresh queue, close posture, and audit trail after restart and refresh.

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
  - [ ] For desktop W2-W4 slices, update `desktop-ui-workflow-acceptance-matrix.md` support
        evidence and the matching W2/W3/W4 checklist row together.
