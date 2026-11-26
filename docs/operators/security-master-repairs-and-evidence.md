# Security Master Repairs and Evidence

**Status:** active guidance
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-25

## Purpose and Scope

Use the browser Security Master workspace to discover missing scalar rate units, freeze evidence-backed
repair cohorts, review each revision and explicitly publish successful items. The same workspace shows
retained impact evidence for each governed revision. These are source-implemented procedures;
environment rollout, hosted checks and operator acceptance remain separate evidence.

## Prerequisites

- An authenticated workstation session with Security Master view/modify permissions for the intended
  account, book, fund profile and period. The server supplies current enabled actions and refusal reasons.
- Retained supporting evidence in Evidence Vault, such as the vendor field definition or prospectus.
  A value's magnitude cannot establish its unit. No AI unit suggestion is part of this workflow.
- A different authenticated reviewer from both the retained maker and submitter, assigned to each
  revision workflow. Batch membership does not grant approval or extend another item's receipt.
- Updated database participants configured against the same supported PostgreSQL authority, with
  Security Master 069 and 070 and ledger 083 applied through the normal migration runner. Follow the
  [terms-contract rollout](security-master-terms-contract-rollout.md) for compatible node deployment.
  A host without PostgreSQL Security Master and ledger stores reports the repair service unavailable.

## Execution Context

Use desktop Chrome or Edge on Windows against the intended installed host. Open the existing
Accounting Security Master route, `/accounting/security-master?rateRepair=queue`. Confirm the
environment and accounting scope before capturing a proposal. No terminal, provider credentials
or WPF action is required by this browser procedure.

## Procedure

1. In the repair queue, inspect vendor, class and issuer filters and the declared source scope.
   Load further server pages when offered. The scan is live and bounded, not a complete inventory
   snapshot. Keep source issues and missing projections visible; do not treat them as zero records.
2. Anchor a compatible row, then select the intended cohort. Compatibility includes exact source
   field/schema, rate role and evidence applicability. Inspect every original scalar/base version.
   A changed overlapping row blocks that observation and clears selection; refresh discovery before
   capturing it. Bond's fix link opens the existing full coupon-schedule migration instead.
3. Enter the real account/book/fund/period, declared unit and effective date. Attach the retained
   evidence ID and justification, and attest that its field/schema definition applies to this exact
   selected roster. The proposed change preserves each numeric scalar. Freeze the proposal only
   after reviewing the roster and interpretation.
4. Retain the batch ID/link. Stage drafts explicitly, then inspect each item's result and impact
   receipt. Stage or retry only permitted actions; conflicts and failures remain per-item outcomes.
   The impact shows conservative candidate books, known periods and before/after flows where
   calculable. Released-report dependencies are reported as unavailable in this increment.
   Unavailable comparisons and unverified lot coverage are not zero impact.
5. Submit to the intended independent reviewer. That reviewer opens the retained batch, verifies
   the actual matching security/revision/fund/base-version impact receipt and reviews the full previous
   and proposed terms. Approve only the authorized items. A stale dependency requires new review;
   an already approved stale revision needs a fresh draft/workflow.
6. Publish explicitly and inspect each record's Published/Conflicted/Failed outcome. Preserve its
   individual revision, workflow, approval and impact receipt. A batch may finish partially; do
   not describe it as published solely because the command returned successfully.
7. For an individual passport edit outside a batch, enter the intended fund profile before drafting;
   its scope then freezes and server authorization checks ownership. Create and inspect the retained
   impact receipt before approving or publishing.

## Expected Results

The batch retains immutable selected IDs/base versions, scalar strings, unit/evidence scope and
per-item draft/workflow/receipt identity. Server actions reflect current authority, assignment and
version. Recent batches lists at most 200; an older retained ID remains directly readable. A repair
is complete for an item only when its canonical publication result is retained. Impact receipts
supply diagnostic evidence, not journal posting or financial-coverage certification.

## Failure and Recovery

| Symptom | Diagnostic | Safe next action |
| --- | --- | --- |
| Capture acknowledgement lost | Retained batch ID/request ID and visible recovery state | Read that ID or retry the exact frozen request; do not create a new proposal for the same uncertain result. |
| Command acknowledgement lost | Refresh required and per-item retained outcome | Reload the retained batch before another command. |
| Missing evidence, wrong scope or permission | Server refusal/action reason | Correct the authorized source/scope or evidence; do not substitute display names or another item's receipt. |
| Source scan incomplete or projection missing | Scope, page continuation and typed source issue | Continue pages where offered; repair/rebuild the source through its existing procedure before claiming completeness. |
| Record or impact dependency changed | Conflict/stale reason and current versions | Re-read and prepare a fresh proposal; preserve prior review evidence. |
| An item failed while others published | Per-item outcomes and retained receipt links | Retry only explicitly permitted unresolved actions; leave successful publications intact. |
| Repair service unavailable | 501 "service unavailable" problem from the rate-repair endpoints | Confirm the host uses PostgreSQL Security Master, revision and ledger stores on the migrated database; no file-store fallback exists. |

Published terms are corrected through a new governed revision. This workflow does not delete repair
manifests, rewrite immutable evidence, reopen periods or reverse posted journals.

## Evidence and Handoff

Retain the batch link, source/evidence applicability, frozen roster/base versions and each item's
draft, workflow, impact and approval receipt. Record the actual partial/published outcomes and source
gaps for Accounting and Ledger. Store sanitized acceptance evidence through the normal deployment
process; a local mocked browser test or source documentation is not production acceptance.

## Related References

- [Security domain and evidence boundaries](../domain/security.md)
- [Governed reporting operations](governed-reporting-operations.md)
- [Terms-contract rollout](security-master-terms-contract-rollout.md)
