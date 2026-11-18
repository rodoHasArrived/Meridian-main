# ADR-030: Periodic Compounding of Governed Intercompany Interest

**Status:** Proposed; first increment implemented
**Date:** 2026-11-17
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-17
**Deciders:** Repository owner (scope direction recorded as `DEC-IC-COMPOUND-001`)
**Supersedes:** None. It narrows ADR-028's "compounding needs retained capitalization history" boundary.
**Superseded by:** None

## Context

ADR-028 calculates simple interest on posted principal and refuses any loan that declares compounding,
because compounding would otherwise rest on interest that was only computed, never reviewed or posted.
A separate capitalisation journal was considered and rejected (see Alternatives): the counterpart mirror
treats each accounting profile in a journal as its own event group, so a journal that moves interest into
principal across two profiles could never be mirrored and would age as a break.

## Decision

Meridian calculates periodically compounding intercompany interest under rule `intercompany-accrual-v3`,
reading the compounding base only from posted journals. Rule v1 is unchanged for simple loans.

1. **Compounding at each schedule period start.** A loan's declared compounding frequency must equal its
   payment schedule frequency (monthly with monthly, and so on). Daily compounding, at-maturity schedules
   and mismatched frequencies are refused.
2. **The base is posted.** For each schedule period, the slice earns
   `(posted principal + posted accrued interest at the period start) x rate x day-count fraction`. The accrued
   interest is the posted balance of the approved `LoanInterest` profile's receivable (lender) or payable
   (borrower) node at the period start: posted accruals and delivered counterparts, net of settlements and
   reversals. Interest settled before the period starts therefore no longer compounds.
3. **Every earlier day is posted.** The window is refused unless posted, unreversed accruals of the profile
   cover every day from the later of the schedule start and the first posted principal movement up to the
   period start. Nothing compounds on interest that was only estimated or is still in review.
4. **One period per request.** A compounding window stays inside one schedule period, because the next
   period would compound on this window's interest before it is posted.
5. **Pinned facts and staleness.** The evidence retains the posted accrued-interest balance (in the same shape
   as principal evidence), the posted accrual windows and the first principal date. Before a draft is
   submitted, approved or posted, these facts are read again; a reversal, settlement or late accrual since
   drafting stops the draft until it is recomputed. The evidence guard refuses v3 evidence without these
   facts and v1 evidence with compounded slices.

Within a period, interest stays simple; the draft identity of a window is unchanged by compounding.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Contracts | `src/Meridian.Contracts/FundStructure/IntercompanyAccrualDtos.cs`, `src/Meridian.Contracts/Ledger/IntercompanyAccrualEvidenceDtos.cs` | Rule v3, compounding inputs, slice field and retained facts |
| Calculator | `src/Meridian.Application/FundStructure/IntercompanyAccrualPreviewService.cs` | Frequency, coverage and one-period checks; compounding base |
| Source builder | `src/Meridian.FinancialOperations/Intercompany/IntercompanyAccrualSourceBuilder.cs` | Posted windows, compounding facts and staleness check |
| Service and controls | `src/Meridian.Ui.Shared/Services/IntercompanyAccrualDraftService.cs`, `src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.Intercompany.cs` | Resolution and the recheck before review or posting |
| Tests | `tests/Meridian.FundStructure.Tests/IntercompanyCompoundingAccrualPreviewTests.cs` | Base, settlement, coverage, one period, v1 unchanged |
| Tests | `tests/Meridian.Tests/FinancialOperations/Intercompany/IntercompanyCompoundingSourceTests.cs`, `tests/Meridian.Tests/Ui/IntercompanyAccrualDraftServiceTests.Compounding.cs` | Real posted journals and reversals, v3 evidence, guard and staleness |

## Alternatives Considered

### Alternative 1: A capitalisation journal moving interest into principal

**Pros:** principal alone would carry the compounded base.
**Cons:** the journal spans two accounting profiles, which the counterpart mirror cannot pair; both entities
would also need a new reviewed journal type.
**Why rejected:** it would create unmirrorable intercompany journals and aged breaks.

### Alternative 2: Compound on computed interest inside one request

**Pros:** a multi-period window could be accrued at once.
**Cons:** later periods would compound on interest no reviewer has posted.
**Why rejected:** the governed path never builds on unposted amounts.

## Consequences

- Each period must be accrued and posted before the next can be accrued.
- Interest settled before a period starts reduces that period's base; settlement inside a period changes the
  base only from the next period.

## Compliance

The focused tests listed above cover the base, settlement, coverage, the one-period rule, the guard and
staleness. A change to when or on what interest compounds needs a new rule version.

## References

- [ADR-028: Governed Intercompany Loan-Interest Accrual](028-governed-intercompany-interest-accrual.md)
- [Intercompany agreements](../domain/intercompany-agreements.md)
