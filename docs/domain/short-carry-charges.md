# Short carrying charges

**Status:** active guidance
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-25

An open short lot accrues two carrying charges: the **stock borrow fee** charged by the lender, and the
**payment in lieu** of a dividend the lender would have received. Neither changes the lot's quantity
or liability basis, so neither is a lot mutation. Each is a period expense paid in cash, booked as a
governed journal that names the short lot it belongs to.

## Queue a charge for review

`POST /api/ledger/journal-automation/short-carry-intake` takes a `RunShortCarryDraftIntakeRequest`: the
fund profile, the ledger book holding the lot, and a reviewed `ShortCarryInstruction`
(`src/Meridian.Contracts/Accounting/Lots/`). The caller never supplies amounts or lines.

`AutomatedJournalIntakeRunner.RunShortCarryIntakeAsync` (`src/Meridian.Ui.Shared/Services/`):

1. Confirms the ledger book belongs to the requested fund profile.
2. Reads the lot of record from the book and proves the instruction against it with
   `ShortCarryPostingGuard.RequireLot`: same lot identity, Short direction, still open, charged quantity
   within the open quantity, and the book's functional currency.
3. Projects the journal with `ShortCarryProjector.Lines` against the lot's liability carrying account and
   wraps it with `ShortCarryDraftBuilder` (kind `ShortCarryCharged`).
4. Queues the draft through the automated-journal intake as an `Expense` workbench draft. Nothing posts
   until the draft is reviewed, approved and posted through the manual journal workbench lifecycle.

A refusal from steps 1–3 returns `409 Conflict` with the reason; a host without the ledger journal store
fails the run rather than queuing nothing.

## What the journal contains

| Charge | Debit | Credit | Amount | Effective date |
| --- | --- | --- | --- | --- |
| Borrow fee | Stock Borrow Fee Expense | Cash | quantity × lender reference price × annual rate × accrued days ÷ 360 or 365 | last accrued day |
| Payment in lieu | Dividend Expense | Cash | quantity × declared gross dividend per unit | pay date |

Amounts are rounded at ledger currency precision; a charge that rounds to zero is refused. Charges are
functional-currency only; a foreign-currency charge needs the separate foreign-currency short treatment
and is refused.

The draft's idempotency key combines the lot record, the charge kind, the accrual interval and a digest
of the instruction, so re-running the same reviewed instruction queues nothing new, and a changed input
is a distinct charge. Tags retain the lot, period, quantity, model version and the lender statement or
dividend notice the inputs came from.

## Current limits

- The queued draft can be edited before approval like other automated drafts; approval review is the
  control that keeps it equal to the projection. `ShortCarryPostingGuard.RequireJournal` is not yet run at
  posting time.
- There is no browser or desktop screen for preparing an instruction yet; the route is API-first.
