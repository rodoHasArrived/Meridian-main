# Ledger Currency Backfill

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-19

This is the canonical operator lane for completing the transaction-currency detail that historical
journal legs are missing, and for reading what the ledger will and will not complete on its own.

## Why any leg is missing currency

`V_ledger_026` added transaction currency, both transaction-side amounts, and the FX rate to
`journal_legs` as nullable columns, so legs written before it have them null. Legs written after it
but before PR #2800 have them null too, for a different reason: the shared posting validator rebuilt
each line to settle its dimensions and did not carry the line's currency across that rebuild, so the
detail was constructed and then dropped on the way to the store. Every generated posting reached
that rebuild, so this affects the append path, not only replay.

The append path is fixed. Nothing repaired the legs already retained, and nothing reported them
either — a currency-blind leg reads back as a leg that simply has no currency detail, which is
indistinguishable from one that was never meant to carry any.

## What the repair is allowed to assert

A leg's `debit` and `credit` are the functional amounts the books balance and report on. The repair
reads them and never writes them, so no trial balance, statement, or report moves.

The only fill it applies is the **identity translation**: transaction currency equal to the
functional currency, transaction amounts equal to the functional amounts, FX rate 1. That records
what the leg was denominated in without inventing a rate.

It never fabricates a foreign rate. The rate that applied to a foreign leg is not recoverable from
anything retained, so a leg that may have been foreign is left null and reported rather than
stamped.

## Dispositions

`ledger.journal_leg_currency_backfill_status` holds one row per currency-blind leg, with the
disposition that decides what can be done about it. The survey and the repair both read this view,
so what you review and what the backfill acts on cannot drift apart.

| Disposition | Meaning | Completed by |
| --- | --- | --- |
| `Repairable` | The book has currency-bearing legs and every one is an identity translation at its base currency. | Automatically |
| `UnaffirmedSingleCurrency` | Nothing contradicts single-currency operation and nothing corroborates it — the book has no currency-bearing leg at all. | Operator affirmation |
| `ForeignCurrencyEvidence` | The book does transact in foreign currency, so a blind leg here may be a foreign leg whose rate is gone. | Nothing; stays null |
| `FunctionalCurrencyMismatch` | The book's legs name a functional currency other than the book's own base currency. | Nothing; stays null |
| `UnusableBaseCurrency` | The book's base currency is not a three-letter code. | Fix the book, then re-survey |
| `UnresolvedLedgerBook` | The leg's accounting period was never scoped to a ledger book, so no functional currency resolves. | Nothing; stays null |

Silence is not evidence. A book with no currency-bearing leg at all could have transacted in
anything, which is why `UnaffirmedSingleCurrency` needs a person and not an inference.

## Operator workflow

1. **Survey.** `PostgresLedgerCurrencyBackfill.SurveyAsync` groups every currency-blind leg by
   ledger book and disposition, and reports how many sit in closed periods. Read it before doing
   anything; `IsComplete` means there is nothing left to repair.
2. **Repair what the data determines.** `RepairEvidencedLegsAsync` completes every `Repairable`
   leg and returns the count. `V_ledger_029` runs the same repair once at migration time; re-running
   it matters because a book that had no currency evidence then accumulates it with every posting
   appended since, so the same legs become repairable without anyone asserting anything.
3. **Affirm what only a person can.** For a book left at `UnaffirmedSingleCurrency`, confirm against
   custodian statements and fund documentation that the book has transacted only in its base
   currency, then call `AffirmSingleCurrencyBookAsync` with the currency, your identity, and the
   rationale. Each affirmation is retained in `ledger.journal_leg_currency_affirmations` with the
   number of legs it completed, as the authority for the change.
4. **Re-survey.** Confirm the remaining scopes are only the dispositions nothing can complete.

The affirmation is narrow on purpose. It completes an evidence gap; it never overrules evidence. A
book showing foreign-currency denomination is refused, as is a book whose blind legs the data
already determines — that one needs step 2 and no assertion at all. Naming the currency is itself a
check: it must be the book's own base currency, because an operator who believes otherwise is
describing a different problem, and stamping either code would be wrong.

## What to expect

- **Closed periods are included.** Most affected history is closed, and leaving it unrepaired would
  defeat the exercise. Repairing changes no functional amount, so no closed-period figure moves.
  The survey reports `ClosedPeriodLegs` per scope so you can see what an affirmation covers before
  you sign it.
- **Re-running is safe.** Every repair is guarded by `transaction_currency is null`, so a second run
  completes nothing and reports zero.
- **The surveys and repairs are unscoped scans.** They read every currency-blind leg through the
  disposition view, so run them as maintenance rather than on a request path. The partial index
  `ix_journal_legs_currency_blind` keeps the working set small, and it shrinks toward empty as books
  are repaired.
- **Repairs run at `Serializable`.** A repair racing heavy posting can fail with a serialization
  error; retry it.
- **Run repairs through this lane, not raw SQL.** `V_ledger_030` made `journal_entries` and
  `journal_legs` immutable at the database; the currency repair is the one governed mutation its
  trigger admits, and only from a transaction that has declared itself via the transaction-scoped
  `meridian.ledger_currency_repair` setting. `PostgresLedgerCurrencyBackfill` makes that
  declaration itself, so the workflow above is unchanged — but a hand-written `update` against
  `journal_legs`, even one that only fills currency detail, is rejected with SQLSTATE `55000`
  unless it declares itself the same way and stamps exactly the identity-translation shape onto a
  currency-blind leg.

## Related

- [Fund Operations Persistence Cutover](./fund-ops-persistence-cutover.md)
- [Governed Reporting Operations](./governed-reporting-operations.md)
- [Reconciliation Operations](./reconciliation-operations.md)
