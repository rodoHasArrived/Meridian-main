# W9 Operator Acceptance Record — 2026-08-29

**Status:** active record
**Owner:** Core Team
**Accepting operator:** `rodoHasArrived` (repository owner)
**Decided on:** 2026-08-29
**Original registry decision:** `DEC-W9-ACCEPTANCE-001` in
[`docs/roadmap/data/decision-log.yml`](../roadmap/data/decision-log.yml)
**Reassessment decision:** `DEC-W9-ACCEPTANCE-002` in the same decision log
**Hold-closure decision:** `DEC-W9-ACCEPTANCE-003` in the same decision log (2026-09-01)
**W9-TRUTH-001 owner-exception decision:** `DEC-W9-ACCEPTANCE-005` in the same decision log (2026-09-26)

## What this record is

The roadmap [status taxonomy](../roadmap/status-taxonomy.md) separates `ready_for_acceptance`
("implementation evidence exists, but operator or governance acceptance is not complete") from
`accepted` ("acceptance evidence exists and is linked from the roadmap item"). On 2026-08-29, six
W9 rows were accepted under `DEC-W9-ACCEPTANCE-001` and `W9-ALPACA-004` was deliberately held.
Corrected evidence later showed that `W9-CORPACT-011` had an unmet exit criterion, so the operator
reopened that row on 2026-08-30 under `DEC-W9-ACCEPTANCE-002`. This file preserves the original
decision, the later reassessment, and the closure of the Alpaca hold: six rows are accepted,
`W9-CORPACT-011` is reopened, and `W9-ALPACA-004` moved from held to accepted on 2026-09-01 under
`DEC-W9-ACCEPTANCE-003` once its three caveats were closed in source.

It is not a release certification. Acceptance of a bounded roadmap row does not close the P0
release-certification gate in [`implementation-todo-list.md`](implementation-todo-list.md), and none
of these rows moves to `done` here — `done` additionally requires the release or status
documentation each row's lane owns.

> **Later status change.** `W9-DEMO-002` has since moved to `done`, on 2026-09-16, under
> `DEC-W9-DONE-001` and the closure record in
> [`w9-demo-002-closure-2026-09-16.md`](w9-demo-002-closure-2026-09-16.md). That record also
> documents a correction to the evidence this acceptance was taken on. The other rows below are
> unaffected, and this file remains the acceptance record for all of them.

> **W9-TRUTH-001 reassessment — 2026-09-25.** Issue #2626 was reopened after source review at
> `a03238c69f86c7455e03c0f6be2196d55719a044` found that reconciliation `SaveAsync` omitted the
> entry-provenance guard, the actual legacy rekey path could discard an incoming mark, and
> duplicate-create identity excluded provenance. The original acceptance remains historical
> evidence; the row moved to `in_progress` pending correction, validation, and review. This status
> was superseded by the correction closure below, which was itself superseded by the
> 2026-09-26 review-gate correction and then the explicit owner exception below. Other rows'
> decisions are unchanged.

## W9-TRUTH-001 correction closure — 2026-09-25 (superseded)

The reconciliation correction merged in [PR #3000](https://github.com/rodoHasArrived/Meridian-main/pull/3000)
as `f96917619ea357aad3759449d9ea69bb6596a9b5`. Saves and actual legacy rekeys now enforce
entry provenance, retained non-real marks cannot be removed by renaming source fields, and
normalized provenance participates in duplicate-create identity. New migration audit entries
retain the original request hash so exact retries survive restart and stronger inherited marks.

All nine hosted workflows passed on head `432d8882bd6bcafe730408d6a64ba32600921c56`, including
[Meridian CI run 36209318392](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36209318392),
Windows Desktop Build, Golden Path Validation, CodeQL, and both WPF validation workflows.
The focused repository suite passed 106 cases, including 35 new regression cases. Automated
source review found no further correction blockers. Local full CI passed restore using
`DOTNET_PROCESSOR_COUNT=1`, then could not evaluate formatting because Roslyn's named-pipe
client received `SocketException (13): Permission denied`; hosted validation supplied the
integration evidence. No local full-CI pass is claimed.

After receiving the correction checkpoint, the user instructed: "Go ahead and merge the completed
work and then move on to implement the remaining open items." PR #3001 restored the bounded
`accepted` posture on that instruction, the merged correction, and the original acceptance evidence
above. It merged as `13f2a11df9fb95c548a50c9e60449fee2353dfb4` at 2026-09-26 03:15:00 UTC
and closed issue #2626 one second later. That closure did not satisfy the separately required
independent security/storage and reconciliation-lineage reviews. The status correction below
supersedes it; the code correction remains in place. Invalid legacy cases still require an explicit
non-real mark before repair or rekey.

## W9-TRUTH-001 review gate reopened — 2026-09-26

**Historical disposition, superseded by the [owner exception below](#w9-truth-001-owner-exception--2026-09-26).**
Issue [#2626](https://github.com/rodoHasArrived/Meridian-main/issues/2626) was reopened for the
required independent human reviews. The row moved to `ready_for_acceptance`, with
`evidence_posture: implementation_complete` and `health: yellow`. This preserves the implemented
correction and its validation while withdrawing the unsupported acceptance closure. Generic merge
authorization, the original acceptance decision, CI, and automated review do not discharge the
specific human review requirement.

The review records checked on 2026-09-26 contain no review submissions on PR #3000. PR #3001 has
an automated Codex `COMMENTED` review and the PR author's `COMMENTED` reply resolving a wording
finding. Neither is independent human security/storage or reconciliation-lineage approval. The
last issue comment before closure explicitly left independent human review outstanding.

Machine evidence was completed after the closure. [Production Certification run 36214217538](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36214217538)
passed all four jobs on merge commit `13f2a11df9fb95c548a50c9e60449fee2353dfb4`. Its retained
`production-certification-90-1` artifact (ID `10897155552`, created 2026-09-26 03:21:38 UTC)
has SHA-256 `2b69dcd66f7bf238ed0e25bd037d61e55282471c46fdb1e01d4ee44129f758dd`;
downloaded bytes matched that digest. `skip-evidence.json` records 1,018 Meridian integration tests
and 12 Direct Lending integration tests passed, with zero failures or skips. The artifact contains
TRX, coverage, schema and inventory evidence. These 1,030 integration cases do not include the
106 focused reconciliation repository tests cited above and do not represent a human review.
The run also retains recovery, documentation, and dependency artifacts. GitHub retention currently
expires on 2026-12-25.

The separate retained review archive is named
`meridian-3001-2626-certification-review-evidence-2026-09-26.zip`, created 2026-09-26 04:02:55 UTC
(1,998,437 bytes). Its existence was confirmed, but its contents could not be inspected during this
reassessment because retrieval failed. The directly downloaded GitHub artifact supplies the
verified certification evidence above; no independent review is inferred from the archive's name.

The reopened gate required dated, named independent human verdicts for both review lanes:

- **Security/storage:** provenance admission before mutation, retained-mark monotonicity, durable
  case/audit consistency, and refusal of invalid legacy repair paths.
- **Reconciliation lineage:** normalized create identity, the original migration request hash,
  exact retries across restart and stronger inherited marks, and compatibility of the legacy
  migration comparison path.

Each verdict was to identify the reviewed source (`432d8882bd6bcafe730408d6a64ba32600921c56`,
merged by #3000 as `f96917619ea357aad3759449d9ea69bb6596a9b5`), the certification commit/run
above, the evidence inspected, and any findings and their disposition. The required next step was
to link both verdicts here and from #2626 and resolve blocking findings before reconciling acceptance.
That pending disposition is superseded only for this correction by the owner exception below. A later change to
the reviewed source requires a fresh assessment of the evidence boundary. No `done` transition or
release certification is claimed here.

## W9-TRUTH-001 owner exception — 2026-09-26

**Current disposition:** `accepted`, `evidence_posture: complete`, `health: green`, under
`DEC-W9-ACCEPTANCE-005`. Issue [#2626](https://github.com/rodoHasArrived/Meridian-main/issues/2626)
is closed. This restores bounded roadmap acceptance only.

[PR #3003](https://github.com/rodoHasArrived/Meridian-main/pull/3003) merged as
`6bd732dcbc83db70e97c18f7ecca0e0adce2a789` at 10:36 UTC. The repository owner then
[attested to completing their own human review](https://github.com/rodoHasArrived/Meridian-main/issues/2626#issuecomment-5845544214)
at 10:37 UTC (03:37 Phoenix time). At 10:39 UTC (03:39 Phoenix time), the owner
[explicitly instructed that the independent-review requirement be marked satisfied](https://github.com/rodoHasArrived/Meridian-main/issues/2626#issuecomment-5845555204).
That decision accepts the completed owner self-review in place of the previously requested
independent non-author reviews for the PR #3000 provenance correction / #2626 gate.

| Review lane | Current gate disposition | Basis |
| --- | --- | --- |
| Security/storage | Satisfied by owner-authorized exception | Completed owner self-review and explicit owner instruction linked above |
| Reconciliation lineage | Satisfied by owner-authorized exception | Completed owner self-review and explicit owner instruction linked above |

**Independent non-author verdicts were not obtained.** The owner is the implementation and
acceptance author. No detailed lane findings, inspected-artifact list, or specific reviewed SHA
was supplied with the self-review attestation; none is inferred. The retained certification and
focused-test evidence above remains unchanged. The earlier requirement and
[two-lane review brief](https://github.com/rodoHasArrived/Meridian-main/pull/3003#issuecomment-5845473719)
remain historical evidence; this exception supersedes their pending review-gate disposition for
this correction only.

The exception does not change repository rules, branch protections, CI requirements, other review
gates, or release certification. The row does not move to `done`, and the P0 release-certification
gate remains open. No fresh test execution or current-head release certification is asserted by
this status reconciliation.

## W9-PAPER-003 final-price correction — 2026-09-25

Source verification for issue #2628 found that both paper gateways rounded prices to tick size
after matching admission. A buy at an observed print and limit of 100.006 could therefore become
a 100.01 fill on a 0.01 tick, violating both the observed envelope and the limit. The equivalent
sell-side rounding could cross the lower bound. The earlier property suite tested the matcher,
while its gateway cases omitted Security Master tick sizes, so it did not catch this final step.

The shared final-price helper now keeps a rounded tick only if it remains positive, within the
captured envelope, and within a limit or stop-limit order's price constraint. Otherwise it retains
the admitted observed price; best-effort tick rounding cannot fabricate a price outside those
bounds. Costs use the final price. `PaperGatewayTickSizeBoundaryTests` exercises both gateways,
all four supported order types, limit breaches inside a wider envelope, valid rounding, and
resting-order execution. Against the original code, 28 regressions failed and four valid-rounding
controls passed. Validation of the correction and hosted merge evidence belong to its PR.
The historical bounded acceptance remains recorded; this is not a release-certification claim.

## Recorded acceptances (six)

| Row | Priority | Accepted on the evidence of |
| --- | --- | --- |
| `W9-TRUTH-001` | critical | Fail-closed simulation labeling across both workstations, startup refusal of in-memory durable-role bindings, and blocking provenance marks at the ledger, reconciliation, and report-pack boundaries. |
| `W9-DEMO-002` | critical | One documented command provisioning five domains over durable file-backed stores, idempotent re-seeding, restart-modelled durability per domain, and the demo-smoke CI lane. |
| `W9-PAPER-003` | critical | Shared documented `paper-match/1` matching policy and `paper-cost/1` cost model, the FsCheck-backed envelope regression suite, and promotion evidence recording both model versions. |
| `W9-REPORT-005` | high | Deterministic client-presentable PDF/XLSX with retained hash and provenance manifests, plus the bespoke partners-capital layout tied to ledger-backed NAV. |
| `W9-NAV-006` | high | Unitized NAV per share class with an auditable movement-level trail, the fee/waterfall/commitment kernels, and the golden-file worked-example pack computed independently of the implementation. |
| `W9-ALPACA-004` | high | Accepted 2026-09-01 under `DEC-W9-ACCEPTANCE-003` on closure of the three held caveats (see [Held back, then closed](#held-back-then-closed) below): the authenticated `trade_updates` stream with its durable content-hashed inbox, the reconnect REST reconciliation with an overlap window, the OMS fill loop with exactly-once accounting handoff, and the restart adoption of untracked fills as the broker's own executed increment. |

The table preserves historical acceptance evidence. `W9-TRUTH-001` has returned to bounded
acceptance under the owner exception above, and `W9-DEMO-002` reached `done` under its later closure record. The current
registry, rather than this historical table, owns each row's status and evidence posture.
`DEC-W9-ACCEPTANCE-001` originally accepted
`W9-CORPACT-011` as a sixth row, but `DEC-W9-ACCEPTANCE-002` supersedes that disposition for that row
only after the corrected approval-lane evidence described below. The registry remains live status.

### Original operating envelope recorded with `DEC-W9-ACCEPTANCE-001`

`DEC-W9-ACCEPTANCE-001` originally accepted `W9-CORPACT-011` on the explicit understanding that
**unsupported and policy-dependent corporate-action branches remain deliberate blocked outcomes**.
Meridian does not coerce them into a generic sale or exchange, and a blocked case is a valid terminal
state rather than a defect. That posture remains the intended operating envelope, but it did not
cover the universal approval-lane gap found after the decision.

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
by the shipped implementation, since posting cannot occur at all. The initial post-decision
correction lowered the row from `complete` to `implementation_complete`; the operator reassessment
below reopens the row because the missing lane is implementation work, not acceptance paperwork.

This is a **universal workflow gap**, categorically different from the unsupported-branch reservation
above: that one reserves specific action types as blocked outcomes by design, whereas this blocks the
approval lane for every case regardless of action type. The reservation above does not cover it.

The block itself is deliberate and defensively implemented — it is honest engineering, not a bug.
What was wrong was the roadmap summary presenting the lane as delivered.

## Reopened on corrected evidence

On 2026-08-30, the operator adopted `DEC-W9-ACCEPTANCE-002`, which supersedes
`DEC-W9-ACCEPTANCE-001` **only for `W9-CORPACT-011`**. The row returns to `status: in_progress`,
`evidence_posture: in_progress`, and `health: red`. Exit criterion four remains unchanged, and
acceptance remains open until the approval and posting lane is reachable and the criterion is
implemented and evidenced. The other five acceptances recorded above remain in force, and the
`W9-ALPACA-004` hold below is unchanged.

## Held back, then closed

**`W9-ALPACA-004` was not accepted on 2026-08-29.** It stayed `ready_for_acceptance`. Its own
summary recorded three caveats, discovered by automated review on 2026-08-10 and all pre-existing
rather than introduced by its acceptance candidate. They were follow-up candidates rather than
exit-criterion regressions, which is why the row was not failed — but they were unresolved
behaviour in the live order feedback path, so acceptance waited on them:

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

Accepting a row whose caveats sit on the path from broker fill to ledger would have put an operator
signature on exactly the evidence chain those caveats qualify. `DEC-W9-ACCEPTANCE-001` set the
standing condition: the row is accepted once they are closed or explicitly waived on the record.

### Closure — 2026-09-01, `DEC-W9-ACCEPTANCE-003`

On the repository owner's direction to resolve the hold, all three caveats were **closed in source
rather than waived**, and the row moved to `accepted` with `evidence_posture: complete`:

1. **Idle-account submission block — closed.** `AlpacaTradeUpdatesClient.IsHealthy` no longer reads
   trade-update recency as liveness. Healthy now means the socket is open, the subscription is
   authenticated and the post-connect REST reconciliation has completed, and no durable-state
   failure is outstanding. Transport liveness comes from the WebSocket keep-alive (the client pings
   on half the stale window and the runtime aborts the connection when a pong does not arrive
   within it), so a dead transport loses `Open` while an order-free account that is silent for
   hours stays healthy and keeps submitting. Transport failures are tracked apart from
   durable-state failures and cleared on a successful reconnect; a refused stream authorization is
   named rather than left to look like idleness.
2. **No backfill overlap window — closed.** Reconnect FILL-activity backfill starts
   `AlpacaBrokerageGateway.FillBackfillOverlap` (fifteen minutes) behind the acknowledged
   watermark. A fill the stream skipped beneath an already-acknowledged newer event is recovered
   at its exact per-fill economics; re-read activities carry their stable ids and the durable inbox
   admits each once. Proven by
   `GatewayReconciliation_FillBackfill_StartsAnOverlapWindowBeforeTheWatermark`.
3. **Restart handoff gap — closed.** `ExecutionReport` carries the broker event's own executed
   quantity (`LastFillQuantity`, stamped from the trade update's `qty` and the FILL activity's
   `qty`, omitted from serialized payloads when null and excluded from inbox identity so existing
   durable records are unaffected). A fill for an order the restarted OMS does not track is adopted
   into tracked state and booked through the same durable accounting handoff as every other fill,
   as exactly that increment and never the cumulative part a previous host already posted. It posts
   **unattributed to the posting scope**, because the original fund attribution is not recoverable,
   and an `UntrackedFillAdopted` audit entry flags it for operator review. A report without the
   per-event quantity is deliberately not booked; that refusal is logged at error level and audited
   as `UntrackedFillNotBooked` instead of being acknowledged quietly. Proven by
   `RestartReplay_FillForAnOrderTheNewHostNeverTracked_ReachesAccountingAsTheEventIncrementOnly`.
   Tightened on 2026-09-02 after the merge review: adoption books only what the live book can
   establish. A buy that opens or adds to a long is established by its own fill price, and a
   reduction (a sell within a known long, a buy within a known short) by the lot it reduces; a
   sell into no long, or either side reversing through zero, cannot be told from the close of a
   position lost with the previous host and is retained as `UntrackedFillNotBooked` for the
   activity-sync lane rather than booked as a phantom short-open with zero-gain proceeds. And
   because the durable inbox guarantees admission, not booking, and delivers out of order, an
   adopted order remembers the earlier quantity its adoption assumed booked; an earlier fill of it
   delivered after the event that adopted it books its own quantity against that gap instead of
   being suppressed by the order's cumulative. Proven by
   `RestartReplay_UntrackedSellWithNoKnownLong_IsRetainedRatherThanBookedAsAShort`,
   `RestartReplay_UntrackedSellAgainstAKnownLong_BooksTheCloseAgainstItsLot`,
   `RestartReplay_EarlierIncrementDeliveredAfterTheCompletionThatAdoptedTheOrder_IsStillBooked`,
   and `UntrackedFillPositionContextTests`.

**What this acceptance is of.** The adoption policy in item 3 is the one judgement the closure
makes that the operator may wish to revisit: a fill on the connected account for an order this host
never placed — a restart survivor or an out-of-band order — now posts to the posting scope rather
than being acknowledged without booking, when and only when the book can establish what the fill
did to it. The alternative left real fills off the ledger with only a warning log. It is recorded
here so that the acceptance is of that behaviour, not of silence.

## Rows not in scope for this record

`W9-SAFETY-007`, `W9-GOV-008`, and `W9-INGEST-009` are `in_progress` and were never candidates
here. `W9-ASSET-010` is already `done`. The 2026-09-01 change that closed the Alpaca hold also
discharged `W9-SAFETY-007`'s criterion one (the trip/submission race), re-ran its criterion-three
sweep, and recorded the Windows WPF build-and-test result, moving that row to
`ready_for_acceptance`; its registry entry carries the follow-up candidates (the OCO reservation
arithmetic and the browser's absent breaker control) for its acceptance review to weigh. The
Windows evidence was re-run on 2026-09-02 against the tree that carries every WPF change in the
sweep (Targeted Test run 33596174759, `wpf-dev-loop` on `windows-latest`, head `0891ca6ca`,
covering `TradingSafetyCommandTests`, `EventReplayViewModelTests`, and
`PositionBlotterViewModelTests`), superseding the earlier run that predated the blotter change.
