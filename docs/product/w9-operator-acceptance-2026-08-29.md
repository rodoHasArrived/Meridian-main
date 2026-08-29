# W9 Operator Acceptance Record — 2026-08-29

**Status:** active record
**Owner:** Core Team
**Accepting operator:** `rodoHasArrived` (repository owner)
**Decided on:** 2026-08-29
**Registry decision:** `DEC-W9-ACCEPTANCE-001` in
[`docs/roadmap/data/decision-log.yml`](../roadmap/data/decision-log.yml)

## What this record is

The roadmap [status taxonomy](../roadmap/status-taxonomy.md) separates `ready_for_acceptance`
("implementation evidence exists, but operator or governance acceptance is not complete") from
`accepted` ("acceptance evidence exists and is linked from the roadmap item"). Six W9 rows carried
implementation evidence and had been waiting on an operator decision. This file **is** that
acceptance evidence: it records the decision, who made it, what was accepted, and what was
deliberately held back.

It is not a release certification. Acceptance of a bounded roadmap row does not close the P0
release-certification gate in [`implementation-todo-list.md`](implementation-todo-list.md), and none
of these rows moves to `done` here — `done` additionally requires the release or status
documentation each row's lane owns.

## Accepted

| Row | Priority | Accepted on the evidence of |
| --- | --- | --- |
| `W9-TRUTH-001` | critical | Fail-closed simulation labeling across both workstations, startup refusal of in-memory durable-role bindings, and blocking provenance marks at the ledger, reconciliation, and report-pack boundaries. |
| `W9-DEMO-002` | critical | One documented command provisioning five domains over durable file-backed stores, idempotent re-seeding, restart-modelled durability per domain, and the demo-smoke CI lane. |
| `W9-PAPER-003` | critical | Shared documented `paper-match/1` matching policy and `paper-cost/1` cost model, the FsCheck-backed envelope regression suite, and promotion evidence recording both model versions. |
| `W9-REPORT-005` | high | Deterministic client-presentable PDF/XLSX with retained hash and provenance manifests, plus the bespoke partners-capital layout tied to ledger-backed NAV. |
| `W9-NAV-006` | high | Unitized NAV per share class with an auditable movement-level trail, the fee/waterfall/commitment kernels, and the golden-file worked-example pack computed independently of the implementation. |
| `W9-CORPACT-011` | high | Durable corporate action case processing with a persisted provider release gate rechecked at acceptance, idempotent actor-attributed transitions, immutable journals with correction lineage, and golden ledger and price-adjustment coverage. **See the approval-lane limitation below, found after this decision was taken.** |

Each accepted row links this file as acceptance evidence and moves to `status: accepted`. Five of the
six carry `evidence_posture: complete` and `health: green`. **`W9-CORPACT-011` carries
`implementation_complete` and `health: yellow` instead**, because of the approval-lane limitation
recorded below — found after this decision was taken. `yellow` is the taxonomy's value for an
unresolved acceptance gap; `green` asserts no known blocker for the current target, which an unmet
exit criterion contradicts. Any summary that reports a single posture or a single health value for
all six rows contradicts the registry, which owns live status.

### Accepted with a noted operating envelope

`W9-CORPACT-011` is accepted on the explicit understanding that **unsupported and policy-dependent
corporate-action branches remain deliberate blocked outcomes**. Meridian does not coerce them into a
generic sale or exchange, and a blocked case is a valid terminal state rather than a defect. That
posture is the intended operating envelope, not a gap to be closed by widening the taxonomy.

### Correction — approval lane unreachable, found after this decision

**This limitation was discovered by automated review on 2026-08-29, after the acceptance decision
recorded here was taken, and it was not part of what the operator was shown.** It is recorded here
rather than quietly fixed because it narrows what this acceptance actually covers.

The row's summary, as written at the time of the decision, described the case as linking source
events through "projected consequences, approvals, posting, and correction lineage". The approval and
posting half of that is modelled in the contract but is not reachable in the shipped implementation:

- `CorporateActionOperationsService.TransitionCaseAsync` refuses **every** transition to
  `ReadyForApproval` with `ProjectionStale`, because the durable exact-version accounting projection
  authority is not persisted.
- `PostgresCorporateActionOperationsStore.Cases` refuses the same transition independently, so the
  block holds at the store as well as the service.
- `CanApproveAccounting` is returned `false` by both the service and the operator endpoint, and
  `ReadyForApproval` is filtered out of the available transition targets.

No case can therefore reach `ReadyForApproval`, `Approved`, `Scheduled`, `Posted`, `Reconciled`,
`Reported`, or `Closed`. Exit criterion four — which describes posting refused without approved
policy coverage, an open period, balanced journals, and required maker-checker approval — is not met
by the shipped implementation, since posting cannot occur at all. The row's `evidence_posture` is
therefore corrected from `complete` to `implementation_complete`.

This is a **universal workflow gap**, categorically different from the unsupported-branch reservation
above: that one reserves specific action types as blocked outcomes by design, whereas this blocks the
approval lane for every case regardless of action type. The reservation above does not cover it.

The block itself is deliberate and defensively implemented — it is honest engineering, not a bug. What
was wrong was the roadmap summary presenting the lane as delivered. **The operator may wish to revisit
this acceptance**, since the caveat is closer in kind to the ones that led `W9-ALPACA-004` to be held
than to the branch reservation on which this row was accepted. The status is left at `accepted`
because reversing a recorded operator decision is not the reviewer's call; the record is corrected so
that the decision can be revisited on accurate information.

## Held back

**`W9-ALPACA-004` is not accepted.** It remains `ready_for_acceptance`. Its own summary records
three caveats, discovered by automated review on 2026-08-10 and all pre-existing rather than
introduced by its acceptance candidate. They are follow-up candidates rather than exit-criterion
regressions, which is why the row is not failed — but they are unresolved behaviour in the live
order feedback path, so acceptance waits on them:

1. **Idle-account submission block.** The hosted submission health gate treats trade-update recency
   as stream liveness, so an idle or order-free live account blocks new submissions once the
   30-second stale window lapses. Submission-side only.
2. **No backfill overlap window.** Reconnect FILL-activity backfill starts exactly at the
   acknowledged watermark. A fill the stream skipped beneath an already-acknowledged newer event is
   recovered only through the order-snapshot lane, which restores quantity at snapshot
   average-price attribution rather than exact per-fill economics.
3. **Restart handoff gap.** A durably admitted fill replayed into a freshly restarted host is
   acknowledged without reaching the accounting handoff when the restarted OMS no longer tracks its
   order, leaving recovery to the brokerage activity-sync lane.

Accepting a row whose caveats sit on the path from broker fill to ledger would put an operator
signature on exactly the evidence chain those caveats qualify. The row is accepted once they are
closed or explicitly waived on the record.

## Rows not in scope for this record

`W9-SAFETY-007`, `W9-GOV-008`, and `W9-INGEST-009` are `in_progress` and were never candidates
here. `W9-ASSET-010` is already `done`.
