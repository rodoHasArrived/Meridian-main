# ADR-033: Cross-Company Reciprocal Balance Confirmation and Adjustment Drafts from Resolved Disputes

**Status:** Accepted 2026-11-25; first increment implemented
**Date:** 2026-11-25
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-25
**Deciders:** Repository owner (`DEC-IC-CONFIRM-003` accepted 2026-11-25)
**Supersedes:** None. It extends `DEC-IC-CONFIRM-001` and works beside ADR-032.
**Superseded by:** None

## Context

Reciprocal balance confirmation (`W12-ICCONFIRM-001`) left two gaps out of its first slice:

- **Pairs spanning two companies of one tenant.** The service required both ledger books to belong to the caller's
  single company, and the store, its lock and its uniqueness were keyed by that company. A lender in one company and a
  borrower in another could not confirm their balances, even though the approved, tenant-scoped accounting profile
  already maps both books.
- **Correction after a dispute.** Resolving a dispute only recorded `Resolved`. The operator then keyed a manual journal
  by hand, with no link back to the dispute, and could pick any account.

## Decision

1. **The approved profile links the two companies.** A confirmation may span two companies of one tenant when the
   approved accounting profile maps one book in each. The caller's company must hold at least one of the books to
   propose or read the pair; a book of another tenant is never linked. Each side's balance is read from its own
   book's schedule in the scope of the company that owns that book, and only totals, journal identities and the
   schedule hash are retained.
2. **Each company acts only on its own side.** The confirmation records each side's owning company and is visible to
   either. A company confirms, disputes, resolves and drafts adjustments only for the side whose book it holds. The
   same-person rule still applies across companies: one actor cannot confirm both sides.
3. **A cross-company dispute is resolved by both sides.** Each side records its own resolution, with a reason and by
   someone other than the person who raised the dispute. The status stays `Disputed` until both have, so neither
   company can settle the other's position. Same-company pairs keep the single resolution that needs authority over
   both books.
4. **Same-company records are unchanged.** Records retained before this decision, and same-company pairs, leave the
   party company fields empty and stay visible to their proposing company alone. Ledger migration `083` adds two
   nullable columns and indexes and rewrites no retained version.
5. **A resolved dispute may pre-fill an ordinary journal draft.** For a `Resolved` confirmation, at the exact version
   and balance digest shown, an operator of one side may prepare a balanced draft in that side's book: a signed amount
   on the side's mapped intercompany receivable or payable, carrying the approved profile's agreement, binding and
   transaction identities, and an offset on a non-intercompany account of that book. The draft identity is
   deterministic per confirmation, side and resolution, and a retry returns the retained draft. Evidence links name
   the dispute and the resolution. The draft is **never submitted, approved or posted** by this path; it goes through
   the ordinary journal-workbench review and posting authority. Cross-currency adjustments are refused here and stay
   in the journal workbench.
6. **Close readiness needs no new gate.** Each company's book summary already feeds the ADR-032 gate, and now includes
   cross-company profiles that map that book.

## Implementation Links

| Area | Paths | Notes |
| --- | --- | --- |
| Contracts | `src/Meridian.Contracts/Ledger/IntercompanyBalanceConfirmationDtos.cs` | Party company fields, `IsCrossCompany`, `IsVisibleTo`, adjustment request |
| Rules and service | `src/Meridian.FinancialOperations/Intercompany/IntercompanyBalanceConfirmationRules.cs`, `src/Meridian.FinancialOperations/Intercompany/IntercompanyBalanceConfirmationService.cs` | Per-side cross-company resolution; per-side company scope |
| Stores | `src/Meridian.Storage/Ledger/PostgresIntercompanyBalanceConfirmationStore.cs`, `src/Meridian.Storage/Ledger/FileIntercompanyBalanceConfirmationStore.cs`, `src/Meridian.Storage/Ledger/Migrations/V_ledger_083__intercompany_confirmation_party_companies.sql` | Visibility by either party company; company-less lock key |
| Adjustment drafts | `src/Meridian.Ui.Shared/Services/IntercompanyConfirmationAdjustmentDraftService.cs`, `src/Meridian.Ui.Shared/Endpoints/IntercompanyBalanceConfirmationEndpoints.cs` | `POST /api/ledger/intercompany/confirmations/{id}/adjustment-drafts` |
| Browser | `src/Meridian.Ui/dashboard/src/screens/accounting-screen.entity-confirmations.tsx` | Company labels, per-side resolution, adjustment fields |
| Desktop | `src/Meridian.Ui.Services/Services/Accounting/IntercompanyBalanceConfirmationWorkspace.Adjustments.cs`, `src/Meridian.Wpf/Views/IntercompanyBalanceConfirmationControl.xaml` | Same, over the shared route |
| Tests | `tests/Meridian.Tests/FinancialOperations/Intercompany/IntercompanyBalanceConfirmationCrossCompanyTests.cs`, `tests/Meridian.Tests/Ui/IntercompanyConfirmationAdjustmentDraftServiceTests.cs`, `tests/Meridian.Tests/Storage/IntercompanyBalanceConfirmationStoreTests.cs` | Service, drafts, stores (file and PostgreSQL) |

Implemented on 2026-11-25 with synthetic fixtures. The repository owner accepted `DEC-IC-CONFIRM-003` on
2026-11-25. Acceptance adopts the decision; it is not production certification, and evidence remains synthetic.

## Rationale

The accounting profile is already the reviewed, tenant-scoped statement that two books are parties to one agreement,
so it is the natural authority for linking companies. Keeping every action per side keeps each company's
controls intact. Making the correction a pre-filled ordinary draft gives the operator the right account and the
dispute link without creating a new posting path.

## Alternatives Considered

### Alternative 1: A tenant-level role that may act for both companies

Rejected: it would let one company settle the other's position and weaken the per-company scope every other
intercompany control relies on.

### Alternative 2: Post the adjustment automatically on resolution

Rejected: postings stay with the journal workbench's maker-checker review; a resolution states how a dispute was
settled, not the reviewed journal.

## Consequences

- Same-company confirmations behave exactly as before.
- A company sees a cross-company pair it is party to, and the counterpart company's balance totals for it.
- A cross-company dispute stays open, and blocks close under an active ADR-032 policy, until both sides record a
  resolution.
