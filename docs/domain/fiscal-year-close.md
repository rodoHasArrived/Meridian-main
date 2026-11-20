# Fiscal-year close

**Status:** active guidance
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-20

Roadmap item: `W12-YEND-001` (Fiscal year-end close and opening-balance roll-forward).

## What exists today (slice 1: read-only close plan)

`GET /api/ledger/books/{ledgerBookId}/fiscal-years/{fiscalYear}/close-plan` returns a
`FiscalYearClosePlanDto` for one ledger book. The plan is read-only: it never posts, closes,
reopens, or changes period state. It is built by `FiscalYearClosePlanBuilder`
(`src/Meridian.Ui.Shared/Services/`) from retained hard-close snapshots
(`period_close_snapshots`), not from live journals that may have been restated since. Workstations
read it through `IWorkstationFiscalYearCloseApiClient` (`src/Meridian.Ui.Services/`) and do not
recompute balances.

### Readiness

The fiscal year's constituent periods are the book's `accounting_periods` rows with that
`fiscal_year`. The plan is ready only when all of these hold; otherwise it lists named blockers
(`FiscalYearCloseBlockerCodes`) and carries no balances and no plan hash:

- At least one period exists for the year (`no-periods`).
- The periods cover the year with no date gaps, and the year starts the day after the prior
  fiscal year's last period ends (`coverage-gap`). A missing period is never treated as empty.
- Every prior-year period is hard-closed, so opening history is final (`prior-year-not-closed`).
- Every constituent period is `HardClosed` (`period-not-hard-closed`) and has a retained close
  snapshot (`snapshot-missing`) for its current close version (`snapshot-stale`) that passes its
  content-hash and scope check (`snapshot-integrity`).
- No earlier period was re-closed after the final period's snapshot was captured
  (`final-snapshot-superseded`); re-close the final period so its snapshot includes the change.
- Every journal in the final snapshot belongs to a period of this book in this or an earlier
  fiscal year (`unknown-journal-period`, `later-year-journal`).
- The retained population balances (`unbalanced`) and the closing transfers move exactly the
  year's reported income into equity (`income-not-preserved`).

### Plan sections

The final period's snapshot retains the book's whole journal population up to the fiscal year end.
From it the plan separates:

| Section | Meaning |
| --- | --- |
| `PreClosingTrialBalance` | Every retained journal except this year's `ClosingEntry` journals, so revenue and expense show the year's activity. |
| `PostedClosingTransfers` | This year's `ClosingEntry` journals, already posted by monthly or quarterly closes. |
| `ProposedClosingTransfers` | Transfers still needed to zero any remaining revenue or expense at year end. Usually empty, because each hard close already requires those balances to be zero. |
| `PostClosingTrialBalance` | Retained balances plus proposed transfers; no revenue or expense balance remains. |
| `NextYearOpeningBalances` | Post-closing asset, liability and equity balances, carried into next year exactly once. |

`ReportedNetIncome` is measured on the pre-closing trial balance, so closing transfers cannot
erase or create the year's income. `OpeningEquity` is equity from journals before the fiscal year;
`PostClosingEquity` must equal `PreClosingEquity + ReportedNetIncome`. Balances keep account,
financial account and dimensional scope.

`PlanHashSha256` is a SHA-256 over the plan, including each period's snapshot version and hash.
It is stable across read order and store restarts and changes when any retained evidence changes.

### Golden example

The design charter's Section 22.6.3 example is covered by `FiscalYearClosePlanBuilderTests` and
`FiscalYearClosePlanPostgresTests`: opening equity 1,000 and profit 100 report income of 100 after
transfers and next-year opening equity of 1,100.

## Not yet built

- Posting year-end closing journals through an authorized year-end posting period or adjustment
  route, and a retained close receipt that a retry recovers instead of posting twice.
- Late corrections that keep the original close and create linked successor closes.
- Browser and WPF close-plan screens.
- Carry-forward rules for investor capital, open lots and accruals beyond plain balances.
