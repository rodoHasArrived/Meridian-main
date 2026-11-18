# ADR-029: Governed Intercompany Fixed Management Fees from Structured Terms

**Status:** Proposed; first increment implemented
**Date:** 2026-11-17
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-17
**Deciders:** Repository owner (scope direction recorded as `DEC-IC-FEE-001`)
**Supersedes:** None. It narrows the "fees are not calculated" boundary of ADR-028 for one declared case.
**Superseded by:** None

## Context

Management-fee agreements already retain a structured fixed amount or an annual percentage with a
calculation-base reference. ADR-028 left both uncalculated: a fixed fee declared no period, so whether
"30,000" meant a month, a quarter or a year had to be guessed, and a percentage fee had no measured base.
Each entity therefore booked its side by hand, which is the same asymmetric-break problem the governed
interest accrual removed for loans.

## Decision

Meridian calculates **fixed** intercompany management fees from approved structured terms under rule
`intercompany-fee-v1` and routes the result through the existing review and proposal pipeline. It never
posts on its own.

1. **A declared basis.** Fixed management-fee terms add `FixedFeeBasis`: `PerPeriod` (the amount is earned
   per schedule period) or `PerAnnum` (the amount is per year, measured by a declared `DayCount`, which a
   per-period fee may not carry). The field is null-omitted, so revisions approved before it existed keep
   their exact review hashes. Without it no fee is calculated.
2. **A pure calculator.** `IntercompanyFeeAccrualPreviewService` turns server-resolved legal and profile
   revisions into an amount for a half-open window `[AccrualFrom, AccrualTo)`. It reads no ledger.
3. **The existing profile template.** Fees post under the approved `ManagementFee` accounting profile of the
   same legal agreement, using its receivable, payable, revenue and expense nodes.
4. **Direction from declared roles.** Management-fee agreements require the roles "Service provider" and
   "Recipient". The provider's book debits its fee receivable and credits fee revenue; the recipient's book
   debits fee expense and credits its fee payable. The operator never chooses a direction.
5. **Paired drafts, not paired postings.** The source side is a draft in ordinary journal review; after it
   posts, the existing proposal pipeline delivers the counterpart as a draft into the other entity's review.

### Calculation rule `intercompany-fee-v1`

- Every day accrues under the approved `ManagementFee` profile revision applicable on that day, whose pinned
  legal revision must itself be applicable; the window is cut at every revision and schedule-period boundary.
- **Per period:** a slice earns `fee x covered days / period days`, using the actual days of the generated
  schedule period. A whole period earns exactly the declared fee.
- **Per annum:** a slice earns `fee x day-count fraction`, each fraction measured from its period's start so
  cutting a period never changes its total, as for interest.
- Slices are summed unrounded and rounded once, half away from zero, to the currency's minor unit.
- **Nothing is estimated.** The whole window is refused, with no amount, when any day lacks a single approved
  profile revision or falls outside the declared schedule; when terms are unstructured, declare no basis or
  declare a percentage fee; when the calendar is missing; when a posted, unreversed fee of the profile already
  covers part of the window; or when the source book has no single open period containing the posting date.
- Cross-currency fees convert through the governed FX authority at the posting date or are unavailable.

### Evidence and controls

The fee's evidence (rule, agreement version and hash and profile revision of every slice, schedule periods,
calendar, direction, FX) is retained in the same accrual-evidence fields of the journal draft as interest
evidence, with its digest, and is carried onto the posted journal. The accrual evidence guard dispatches on the
rule: a fee draft must carry exactly the computed debit and credit in the retained direction, and a rebook must
restate the posted fee exactly. Before a fee draft is submitted, approved or posted, the journal workbench
refuses it when a posted, unreversed fee of the profile overlaps its window, and re-resolves every slice's
profile and legal revision. Fee windows use their own transaction identity (`management-fee:`), so they never
collide with interest windows of the same agreement.

### Not decided here

- **Percentage fees.** They need a governed, measured calculation base (for example a period-end balance);
  there is none in the intercompany path.
- **Fee templates on loans.** Commitment and utilisation fees on loans need a separate profile template.
- **Scheduling.** Nothing runs on a timer; an operator requests each fee.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Terms | `src/Meridian.Contracts/FundStructure/IntercompanyStructuredTermsDtos.cs` | `FixedFeeBasis` declaration |
| Contracts | `src/Meridian.Contracts/FundStructure/IntercompanyFeeAccrualDtos.cs` | Computation input, slices, preview and rule version |
| Evidence and guard | `src/Meridian.Contracts/Ledger/IntercompanyFeeAccrualEvidenceDtos.cs` | Retained evidence, requests and the fee draft guard |
| Calculator | `src/Meridian.Application/FundStructure/IntercompanyFeeAccrualPreviewService.cs` | Pure fee calculation and proration |
| Source builder | `src/Meridian.FinancialOperations/Intercompany/IntercompanyFeeAccrualSourceBuilder.cs` | Direction, overlap and source draft |
| Service and routes | `src/Meridian.Ui.Shared/Services/IntercompanyFeeAccrualDraftService.cs`, `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs` | `/api/ledger/intercompany/fees/preview` and `/drafts` |
| Controls | `src/Meridian.FinancialOperations/Intercompany/IntercompanyLedgerPostingAuthority.cs`, `src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.Intercompany.cs` | Authority recheck and overlap before review or posting |
| Desktop | `src/Meridian.Ui.Services/Services/Accounting/IntercompanyManagementFeeWorkspace.cs`, `src/Meridian.Wpf/Views/IntercompanyManagementFeeControl.xaml` | Management fee tab |
| Browser | `src/Meridian.Ui/dashboard/src/screens/accounting-screen.entity-management-fee.tsx` | "Accrue fee" on an approved `ManagementFee` profile |
| Tests | `tests/Meridian.FundStructure.Tests/IntercompanyFeeAccrualPreviewServiceTests.cs` | Calculation, proration, refusals and terms validation |
| Tests | `tests/Meridian.Tests/Ui/IntercompanyFeeAccrualDraftServiceTests.cs`, `tests/Meridian.Tests/FinancialOperations/Intercompany/IntercompanyFeeAccrualSourceBuilderTests.cs`, `tests/Meridian.Tests/Ui/AccountingConfigurationServiceTests.IntercompanyFee.cs` | Direction, guard, overlap, staleness, counterpart mirror and workbench evidence |

## Rationale

A fixed fee is a pure function of declared terms once its basis is declared, so the least risky design is the
one ADR-028 already proved for interest: compute it, pin the evidence, and hand it to existing review, posting
and proposal controls. Declaring the basis, rather than inferring it from the schedule frequency, keeps a
"per quarter" amount on a monthly schedule from being charged three times.

## Alternatives Considered

### Alternative 1: Infer the fee period from the schedule frequency

**Pros:** no new terms field.
**Cons:** an annual fee invoiced monthly, or a monthly fee settled quarterly, would be charged at the wrong
multiple, and nothing in the retained terms would show the assumption.
**Why rejected:** the amount would depend on an unstated assumption.

### Alternative 2: A new fee profile template

**Pros:** fee accounts separate from other management-fee mappings.
**Cons:** the `ManagementFee` template already maps exactly these four roles for exactly this agreement type.
**Why rejected:** a second template would duplicate an existing reviewed mapping.

## Consequences

### Positive
- Both entities receive the same computed fee from the same pinned terms.
- A reviewer can trace a fee to its agreement version, profile revision, periods and rounding.

### Negative
- Existing fixed-fee agreements must be amended to declare a basis before a fee can be calculated.
- A `ManagementFee` profile without a revenue node for the provider, or an expense node for the recipient,
  cannot draft a fee for that side.

### Neutral
- A posted fee stops blocking its window only when a source-linked exact inverse reverses it.
- A counterpart posted from a proposal counts as a fee of its window in the destination book.

## Compliance

The focused tests listed above cover the calculation rule, each refusal, the direction, the evidence guard,
overlap, the authority recheck and the counterpart mirror. Changing proration, rounding or direction requires
a new rule version rather than an edit to `intercompany-fee-v1`.

## References

- [ADR-028: Governed Intercompany Loan-Interest Accrual](028-governed-intercompany-interest-accrual.md)
- [Intercompany agreements](../domain/intercompany-agreements.md)
- [Intercompany contracts and entity controls plan](../engineering/plans/intercompany-entity-controls.md)
