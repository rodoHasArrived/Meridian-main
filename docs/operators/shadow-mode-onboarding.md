---
title: Shadow-Mode Onboarding (Parallel Run)
status: active
owner: financial-operations
reviewed: 2026-11-13
audience: operators
---

# Shadow-Mode Onboarding (Parallel Run)

Roadmap row: `W11-SHADOW-001` (in progress). Workspace: **Accounting → Bounded onboarding workspace**.

Shadow mode lets a new fund run Meridian beside its current administrator or general ledger without
migrating anything. Meridian reconciles its own opening balances, positions, cash and NAV against the
incumbent each period. The book becomes authoritative only after an independent operator promotes it
with a parallel-run certificate attached. Until then the incumbent stays the book of record.

## What a shadow book cannot do

When a workspace's ledger book is designated as a shadow book, the shared book-of-record guard
(`IBookOfRecordGuard`) refuses to let it act as the book of record:

- **Governed report release** returns `409 Conflict` with "cannot be released as the book of record".
  (`ReportingGovernanceService.ReleaseAsync`)
- **Period hard-close** is blocked with `CLOSE_SHADOW_BOOK`. (`ClosePublicationReadinessGuard`)

A **withdrawn** shadow trial also stays non-authoritative. A new workspace may designate the same book
again; two concurrent shadow trials on one book are refused. The store enforces this inside the same
write lease as the save, so a race between two designations returns `409 Conflict` for the loser.

To check a book's status, use
`GET /api/accounting/onboarding/ledger-books/{ledgerBookId}/book-of-record`. It returns
`isBookOfRecord`, the shadow state, the number of clean periods and the unexplained difference count.

## Workflow

1. **Create the onboarding workspace.** Set the scope, required dates, tolerances and independent
   reviewers as usual.
2. **Designate the shadow book.** Only the owner can do this, and it needs a human operator. The
   designation is recorded in the workspace's hash-chained shadow audit trail.
3. **Import the incumbent opening balances** for the workspace start date. Sources combine: each
   import compares only the required kinds it supplies, and the latest import of each source kind
   counts. A bank statement or GL trial balance covers cash balances only, so pair it with an
   administrator pack for positions and NAV. Re-importing a source replaces only that source.
   The three sources:

   | Source kind | Input | Notes |
   |---|---|---|
   | `StatementOpening` | camt.053 or BAI2 file | Cash only. Reads camt.053 `OPBD` or BAI2 `010` (never the available balance `040`). Every opening balance must be dated the start date. Ordinary statement reconciliation still reads closing balances only. |
   | `AdministratorPack` | CSV | Header: `record_type,account,currency,instrument,quantity,market_value,amount,as_of_date`. `CASH` and `NAV` rows use `amount`; `POSITION` rows use `quantity` and/or `market_value`. An empty value is missing, never zero. Covers the kinds its rows contain. |
   | `GlTrialBalance` | Exact retained provider and import IDs | Cash only. Reads the retained external GL import without calling the provider. Only mapped GL accounts are compared. |

   For file sources, map every incumbent account (IBAN, admin account code) to a selected financial
   account with `EXTERNAL=account-id` lines. Rows that already use Meridian account IDs need no mapping.
   Unmapped or duplicated incumbent rows are refused, because rows are never netted.

   Meridian's side comes from its own retained account records for the start date. These are the
   latest balance snapshot (cash, and NAV only when every component is present) and the retained
   position lines.

   The raw file is kept inside the comparison payload, so the opening comparison can be replayed. The
   opening-balance report combines the sources. It marks each line `Matched`, `Explained`, `Break` or
   `Missing`, names the source of each line, and lists any required kind that no source yet covers for
   every selected account. Opening balances are reconciled only when every required kind is covered, no
   source has a missing requirement, and every line is matched or explained. Explain an opening `Break`
   from the same panel as period differences; a `Missing` line must be supplied, not explained.
4. **Capture each required period** with the existing comparison flow. Opening comparisons form their
   own history, separate from the period history.
5. **Explain every remaining difference** with retained evidence:
   - `Timing` needs an expected reversal date. It stops applying to comparisons after that date.
   - `Pricing`, `Treatment` and `SourceData` clear the difference while the compared amounts stay
     the same.
   - `MeridianError` never clears. Fix the books instead.
   - An explanation is tied to the exact Meridian and incumbent amounts. A changed amount needs a new
     explanation.
6. **Get independent review** of the final data revision.
7. **Issue the parallel-run certificate.** This requires reconciled opening balances, every required
   period clean (zero unexplained differences, no missing sources, coverage met) and readiness `Ready`.
   The certificate contains:
   - the opening report,
   - a per-period agreement table (Meridian NAV, incumbent NAV and variance, matched, explained and
     unexplained counts),
   - every explanation,
   - the approving reviews,
   - a frozen readiness packet hash.
8. **Promote** (the cutover). A designated reviewer who is not the workspace owner attaches the
   certificate. The certificate must still match the current data revision. If anything changed after
   it was issued, issue a new one. Promotion flips the designation to `Promoted`, records a
   `ShadowBookPromotionDecisionDto` and appends a `ShadowPromoted` audit entry. After that, release
   and close stop refusing the book.

## API routes

All routes are under `/api/accounting/onboarding/workspaces/{workspaceId}`. They require
`AdminMaintenance` or `ManageFundStructure`, a resolved tenant and company, and a human operator for
every POST.

| Method | Route | Purpose |
|---|---|---|
| POST | `/shadow-designation` | Designate the shadow book |
| POST | `/shadow-designation/withdraw` | Withdraw the trial |
| POST | `/opening-balances` | Import incumbent opening balances |
| GET | `/opening-balances/report` | Opening-balance reconciliation report |
| POST | `/differences/{differenceKey}/explanation` | Explain a difference |
| POST | `/certificates` | Issue the parallel-run certificate |
| GET | `/certificates/{certificateId}` | Read a certificate (integrity-checked) |
| POST | `/promotion` | Promote to book of record |

## Known gaps

- There is no WPF desktop parity for the shadow panel yet (see `W8-WPF-PARITY-001`).
- `CloseReadinessProjectionDto` does not yet carry a shadow contributor. Today the agreement figures
  come from the book-of-record status and the close guard.
- Onboarding workspaces, including shadow state, use the file-backed store
  (`accounting/onboarding-workspaces.json`).
