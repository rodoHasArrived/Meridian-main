# ADR-028: Governed Intercompany Loan-Interest Accrual from Structured Terms

**Status:** Proposed; first increment implemented
**Date:** 2026-11-14
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-14
**Deciders:** Repository owner (scope direction recorded as `DEC-IC-ACCRUAL-001`)
**Supersedes:** None. It narrows the "interest and fees are not calculated" boundary of the
intercompany agreement registry for one declared case.
**Superseded by:** None

## Context

The intercompany agreement registry retains reviewed loan terms, and since the structured-terms
extension it retains a typed rate, day count, compounding rule and schedule. Until now those
declarations produced dates only. Interest itself was booked by hand on each side, which is the
source of gap G3: one entity books a month of interest, the other books a different amount, a
different date or nothing, and consolidation reports an asymmetric break that no control created
and no evidence explains.

The existing pieces already cover most of what a governed accrual needs:

- the schedule kernel, business-day calendars and `ScheduleDayCount` used by the date preview and by
  bond coupon recognition,
- immutable accounting profiles that pin one exact approved legal revision,
- the counterpart proposal pipeline, which retains source facts atomically with the source journal
  and delivers a balanced destination draft into the other entity's own review.

Two things were explicitly excluded. The registry stated that it calculates no interest or fees, and
the `Loan` mirror template refused any operating leg ("the loan mirror supports principal only").
Calculating an amount therefore needs a recorded decision, not just a new service.

## Decision

Meridian calculates intercompany loan interest from approved structured terms and routes the
result through the existing review and proposal pipeline. It never posts on its own.

1. **A pure calculator.** `IntercompanyAccrualPreviewService` turns server-resolved legal revisions,
   profile revisions and principal into an amount for a half-open window `[AccrualFrom, AccrualTo)`.
   It reuses the declared schedule-to-kernel mapping of the date preview and the shared day-count
   kernel. It reads no ledger and creates nothing.
2. **A separately reviewed profile template.** `LoanInterest` is a new accounting-profile template
   for loan agreements. `Loan` continues to mirror principal only. A `LoanInterest` profile must map
   interest income on one party against interest expense on the other, and goes through the same
   profile approval as every other template.
3. **Paired drafts, not paired postings.** The service retains a source-side draft in the source
   book's ordinary journal review. After a reviewer approves and posts it, the existing proposal
   pipeline retains the source facts in the same commit and delivers the counterpart as a draft in
   the destination book's review queue. There is no second route and no automatic posting on either
   side.
4. **Pinned provenance.** Each accrual retains the agreement version and payload hash and the
   profile revision of every slice, the posted principal journals it read, the calendars and kernel
   version, and any FX conversion. The evidence and its SHA-256 digest are retained on the draft,
   cannot be replaced by a client save, are checked against the draft's lines before review, and are
   carried onto the posted journal.

### Calculation rule `intercompany-accrual-v1`

- The window is half-open, matching the registry's `[EffectiveFrom, EffectiveTo)` convention. The
  draft is dated on the last accrued day, `AccrualTo - 1`.
- **Every day accrues under the approved revision applicable on that day.** For each day the
  service resolves the single approved `LoanInterest` profile revision under the same end-of-day
  rule posting uses, and that revision's pinned legal revision must itself be the applicable one.
- The window is cut at every profile-revision boundary, schedule-period boundary and principal
  change. Each slice is `principal x annual rate x day-count fraction`, using its own revision's
  rate, day-count convention and schedule. A slice's fraction is the difference of two fractions
  measured from its schedule period's start, so cutting a period never changes the period's total;
  under 30/360 a principal movement on the 31st cannot add a day.
- **Rate resets and amendments inside a period** are therefore prorated by day: the days before the
  amendment's effective date use the earlier revision, the days from it use the amended one. An
  amendment that has no re-reviewed profile revision pinning it leaves those days without authority
  and the whole window is refused.
- Slices are summed unrounded and rounded **once**, half away from zero, to the agreement
  currency's minor unit. Slices are never rounded individually.
- **Principal comes from the ledger.** It is the posted balance of the approved `Loan` profile of
  the same legal agreement in the source book. A movement dated `d` changes the balance from `d`
  inclusive, so a drawdown earns interest on its own date and a repayment stops earning on its own
  date. A receivable balance makes the source book the lender; a payable balance makes it the
  borrower.
- **Nothing is estimated.** The whole window is refused, with no amount, when any day lacks a
  single approved profile revision, falls outside the declared schedule, or lacks provable
  principal; when terms are unstructured, floating-rate or compounding; when the calendar is
  missing; when the result is not positive; when a posted, unreversed accrual already covers part
  of the window; or when the source book has no single open period containing the posting date.
- **Cross-currency loans** convert the agreement-currency amount into the source book's functional
  currency through the governed FX authority at the posting date. Without that authority the
  accrual is unavailable. The destination side is converted by the existing counterpart delivery
  under the destination book's own FX configuration.

### Not decided here

- **Fees.** A fixed management fee has no declared amount period in terms schema v1, and a
  percentage fee has no measured calculation base. Both stay uncalculated until the terms declare
  what would otherwise be guessed.
- **Floating rates and compounding.** They need a governed fixing authority and retained
  capitalization history.
- **Scheduling.** Nothing runs on a timer. An operator requests each accrual.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Contracts | `src/Meridian.Contracts/FundStructure/IntercompanyAccrualDtos.cs` | Computation input, slices, preview and rule version |
| Evidence and guard | `src/Meridian.Contracts/Ledger/IntercompanyAccrualEvidenceDtos.cs` | Retained evidence, request contracts and the draft guard |
| Calculator | `src/Meridian.Application/FundStructure/IntercompanyAccrualPreviewService.cs` | Pure amount calculation and proration |
| Profile rule | `src/Meridian.Entities/FundStructure/IntercompanyAccountingArrangementPolicy.cs` | `LoanInterest` template validation |
| Source builders | `src/Meridian.FinancialOperations/Intercompany/IntercompanyAccrualSourceBuilder.cs` | Authority by day, ledger principal, overlap check and source draft |
| Service | `src/Meridian.Ui.Shared/Services/IntercompanyAccrualDraftService.cs` | Resolves authorities and retains the draft |
| Endpoints | `src/Meridian.Ui.Shared/Endpoints/IntercompanyOperationsEndpoints.cs` | `/api/ledger/intercompany/accruals/preview` and `/drafts` |
| Tests | `tests/Meridian.FundStructure.Tests/IntercompanyAccrualPreviewServiceTests.cs` | Calculation, proration and refusals |
| Tests | `tests/Meridian.Tests/FinancialOperations/Intercompany/IntercompanyAccrualSourceBuilderTests.cs` | Principal, overlap, draft, guard, counterpart mirror and profile rule |
| Tests | `tests/Meridian.Tests/Ui/IntercompanyAccrualDraftServiceTests.cs` | Service resolution and refusals |
| Tests | `tests/Meridian.Tests/Ui/AccountingConfigurationServiceTests.IntercompanyAccrual.cs` | Evidence retention through the journal workbench |

The calculator, template, source draft, evidence guard, endpoints and the tests above are
implemented. Browser and WPF screens for requesting an accrual are not: the browser can select the
`LoanInterest` template when authoring a profile, and the accrual itself is reachable through the
shared API only. No PostgreSQL-backed or end-to-end post-and-deliver run is recorded for this
increment. This decision does not certify a release or move a roadmap row to done.

## Rationale

The amount is a pure function of facts Meridian already governs, so the least risky design is to
compute it and hand it to controls that already exist. The source draft is an ordinary journal
draft with extra retained provenance; the counterpart is an ordinary counterpart proposal. Review,
segregation, period control, posting authority, retry and break aging all apply unchanged, and an
asymmetric break can only arise where a reviewer on one side has not yet acted, which the existing
break report already shows.

Reading principal from posted journals rather than accepting a typed balance keeps the one input
the terms do not declare inside the same evidence chain.

## Alternatives Considered

### Alternative 1: Post both sides automatically

**Pros:**
- No reviewer effort; no timing break between entities.

**Cons:**
- Removes the destination entity's review and authorization boundary.
- Contradicts the "never auto-post" rule of the proposal stage.

**Why rejected:** The control value of the pipeline is that each entity reviews its own books.

### Alternative 2: Extend the `Loan` template to accept interest legs

**Pros:**
- No new template kind.

**Cons:**
- Existing approved `Loan` profiles would silently gain an interest treatment nobody reviewed.

**Why rejected:** Interest needs its own reviewed income and expense mapping.

### Alternative 3: Operator-entered principal

**Pros:**
- Works before principal is attributed in the ledger.

**Cons:**
- The largest input to the amount would be unverifiable.

**Why rejected:** A typed balance cannot be pinned as evidence.

## Consequences

### Positive

- Both entities receive the same computed amount from the same pinned terms.
- A reviewer can trace an accrual to its agreement version, profile revision, principal journals
  and rounding.
- Mid-period amendments have one stated proration rule.

### Negative

- A loan needs two approved profiles, `Loan` for principal and `LoanInterest` for interest.
- Principal that was never attributed to the `Loan` profile cannot be accrued until it is.
- An amendment requires a re-reviewed profile revision before affected days can accrue.

### Neutral

- Overlap is checked against posted accrual journals of the profile, whatever account an earlier
  profile revision posted them to, when an accrual is previewed or drafted and again by the journal
  workbench before a draft is submitted, approved or posted. Two unposted drafts for overlapping
  windows can coexist; once one posts, the other can no longer advance.
- A posted accrual stops blocking its window only when a source-linked exact inverse reverses it.
  Settling the receivable or payable does not: the balance nets to zero while the interest stays
  posted. A recomputation after a reversal gets a new draft identity; the original is not reused.
- The evidence guard binds the debit and credit direction to the retained lending direction, so a
  balanced draft with both sides swapped is refused.

## Compliance

The focused tests listed above cover the calculation rule, each refusal, evidence retention and the
counterpart mirror. Changing the proration, rounding or principal rule requires a new rule version
rather than an edit to `intercompany-accrual-v1`, because retained evidence names the rule that
produced it.

## References

- [Intercompany agreements](../domain/intercompany-agreements.md)
- [Intercompany contracts and entity controls plan](../engineering/plans/intercompany-entity-controls.md)
- [ADR-025: Contractual Schedule Kernel](025-contractual-schedule-kernel.md)
