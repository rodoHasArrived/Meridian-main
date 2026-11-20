---
title: Book receipt queue
status: active
owner: core-team
reviewed: 2026-11-20
audience: operators
---

# Book receipt queue

Use **Accounting → Book receipt queue** to investigate coupon, interest and principal receipts
for one ledger book. Select the book and an observation date, then choose **Run daily check**.
Opening a retained run reads its saved results; it does not recalculate amounts or change casework.
Daily checks are explicit commands, not an automatically installed schedule.

## Authority and prerequisites

The server resolves tenant and company from the authenticated operator, verifies the book's
retained ownership and checks access to its entity. Reading runs requires scoped ledger-report
read or management permission; capturing a run requires management permission. Administrative
permission does not bypass tenant/company ownership. The request supplies only book and date.

Expected amounts require approved retained book positions and published Asset Operations cash-flow
projections with accepted evidence bound to the exact projection run or cash-flow identity. A
completed calculation, an accepted security description or an illustrative Security Master timeline
does not establish an accepted receipt amount. Terms must resolve to an explicit retained version/hash
or one unambiguous version known when the projection was generated.

Historical positions remain discoverable after maturity or closure. Due and overdue amounts use
the economic state before payment day, so a same-day principal paydown cannot erase a receipt due.
Future amortizing schedules preserve the current ownership share; schedules already bound to a
specific position are not scaled again. Missing sizing, ownership or dated evidence remains incomplete.

Actual cash comes from authorized canonical statement imports with verified completion, source and
mapping hashes, stable provider payment identities and exact account/book scope. Posted amounts
require retained reconciliation matches to the book's posted journal cash. Provider cash can be
observed while its internal posting comparison is still incomplete. Follow
[statement reconciliation operations](statement-reconciliation-report-operations.md) to repair intake
or comparison evidence.

Explicit receipt splits and reversals must survive canonical statement import: preserve the original
provider payment link and the source obligation/projected-flow identities. Use the optional receipt
metadata described in [reconciliation operations](reconciliation-operations.md).
Legacy imports without these fields remain readable with their original hashes, but absent linkage
is not repaired by guessing or editing a normalized sidecar; it remains incomplete evidence.

## Read the amounts and status together

| Field | Meaning |
| --- | --- |
| Expected | Current accepted, holding-sized obligation; previous expectations remain in its history. |
| Received | Allocated provider cash, net of explicitly linked receipt reversals. |
| Posted | Linked, validated journal cash allocations whose postings have not been reversed. |
| Remaining | `max(0, expected − received)`; this is a collection amount, not an unposted balance. |
| Aging | Calendar days after the due date, with zero before or on that date. |
| Owner | Assignee of the linked reconciliation exception at capture time. |

Posted means retained Meridian journal cash. External GL delivery and provider confirmation are
separate controls: accepting an export does not add another receipt or posting. A reconciled row
can still await external GL confirmation required by the book's close or report-release policy.

Amounts stay in their own currencies. Receipt splits must conserve the payment; canonical payment
identity prevents reimports from counting twice. Retained receipt and journal allocations remain
pinned when schedules change. Explicit older projection references resolve only to one retained
accepted obligation for the same security and currency. Unknown or ambiguous references remain
incomplete. Conflicting, excessive or ambiguous allocations produce evidence
issues rather than additional money.

| Status | Operator interpretation |
| --- | --- |
| Scheduled / Due / Overdue | No allocated cash; compare the payment date with the observation date. |
| Partially received | Some cash is allocated and a collection balance remains. |
| Disputed | An amount discrepancy or the linked case's investigation/dispute requires review. |
| Received but unposted | Cash covers the expectation; posted evidence does not yet establish complete reconciliation. |
| Reconciled | Expected, received and posted amounts agree, with complete evidence coverage. |
| Incomplete evidence | Equal amounts cannot establish reconciliation because required evidence is missing. |

Check the evidence indicator even when a row says **Due**, **Overdue** or **Received but unposted**.
Coverage is tracked separately for holdings, projections, receipts and postings, with detailed
provider/account gaps. An empty provider population, missing statement window or unsupported account
inventory is not proof of zero receipts. Such gaps prevent a complete reconciliation claim.

Open a row to inspect terms/projection identities, earlier expectations, individual receipts,
allocations, journal references and source links. Investigate overreceipts and unsupported mappings
through the existing reconciliation workflow; do not change an expectation merely to match cash.

## Terms amendments and retained daily runs

Stable obligation identity prefers an explicit retained source obligation/period ID, then a complete
coupon or interest accrual interval, or terminal principal identity. Moving a payment date therefore
versions the same identified obligation. A dated fallback is used only where stronger identity is
absent; ambiguous schedule rows require source repair. New terms do not rewrite obligations before
their effective date, and an obligation does not disappear merely because a later source omits it.

Each capture retains its scope, date, engine version, source snapshot, coverage, expectation history
and queue results under a content-derived run ID. Identical retained inputs and results reuse the run;
changed evidence or casework can create another immutable observation for the same date. Select a
saved run to reproduce what was observed then, rather than reconstructing it from today's terms.
The file store checks scope and content integrity on read. Preserve damaged retained files and restore
them from trusted backup instead of deleting history to manufacture a clean run.

## Exceptions and reversals

Overdue, partial, disputed and received-but-unposted obligations reuse the canonical reconciliation
exception queue. Existing assignment, evidence, audit history and operator disposition remain there.
Agreement of the three amounts does not automatically resolve or sign off a case.

A linked receipt or journal reversal that breaks reconciliation reopens a closed receipt exception
through the source-observation path, with a version check, retained reversal evidence and an audit
receipt. Each source reversal is consumed once, including when saving the subsequent daily run
fails; a later operator disposition is preserved unless new reversal evidence arrives. A historical
observation cannot reopen later casework. A retained close freeze blocks this
transition: the capture reports a conflict and requires the existing governed period-reopen workflow.
This queue cannot override the freeze, approve a case, execute a payment or create/post a journal.

## API and evidence scope

`GET /api/workstation/accounting/receipt-queue/runs?ledgerBookId=…` lists retained observations;
optional `asOfDate` filters the date. `GET …/runs/{runId}?ledgerBookId=…` reads one saved observation.
`POST …/runs` with `ledgerBookId` and `asOfDate` captures a new observation and may publish or reopen
source-owned exceptions. GET requests have no capture side effects.

Development fixtures use synthetic records. They establish Meridian contract behavior only;
live-provider compatibility remains **live-untested** unless separately evidenced. This procedure
does not claim production certification or verification of a provider connection.
