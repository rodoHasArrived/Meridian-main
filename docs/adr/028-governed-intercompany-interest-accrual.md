# ADR-028: Governed Intercompany Loan-Interest Accrual from Structured Terms

**Status:** Proposed; first increment implemented; floating-rate extension (2026-11-17) implemented
**Date:** 2026-11-14
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-17
**Deciders:** Repository owner (scope direction recorded as `DEC-IC-ACCRUAL-001` and, for floating rates,
`DEC-IC-FLOAT-001`)
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

### Extension (2026-11-17): rule `intercompany-accrual-v2`, floating rates

Recorded as `DEC-IC-FLOAT-001`. Rule v1 is unchanged; v2 adds floating-rate simple interest.

- **Declared reset.** A floating loan's structured terms add `FixingLagBusinessDays` (0 to 10). The
  index fixing for a schedule period is observed that many business days, on the schedule calendar,
  before the period's accrual start and holds for the whole period (set in advance). The field is
  null-omitted, so revisions approved before it existed keep their exact review hashes; a floating
  revision without it cannot accrue, because no fixing date is inferred.
- **Reviewed fixings.** `IntercompanyRateFixingService` retains fixings per tenant, index and
  observation date as append-only versions (fund-structure migration `009`, or an atomic file store).
  An operator proposes a value with its source and justification; it sets rates only after someone
  else approves it. A correction is a new pending value: the approved value stays in force until the
  correction is approved, and a rejection needs a reason. Nothing is fetched from a market feed.
- **Calculation.** Each floating slice's annual rate is the approved fixing of its period plus
  `SpreadBps / 10,000`. Exactly that date's approved value is used: never the nearest, the latest or
  a carried-forward rate. A missing fixing refuses the whole window. Everything else (authority by
  day, proration, cutting, one rounding) is v1's rule.
- **Rule identity.** A window with any floating day computes under v2; an all-fixed window stays v1,
  with unchanged evidence. The draft identity stays keyed on the v1 rule name, so a window never
  gains a second identity. Each floating slice pins its fixing identity, index rate, spread, lag and
  value digest, and the evidence guard refuses a slice whose rate differs from its pin.
- **Staleness.** Before an accrual draft is submitted, approved or posted, every pinned fixing is read
  again; a value re-approved after drafting stops the draft until it is recomputed.

### Not decided here

- **Fees.** A fixed management fee has no declared amount period in terms schema v1, and a
  percentage fee has no measured calculation base. Both stay uncalculated until the terms declare
  what would otherwise be guessed.
- **Compounding, in-arrears or averaged rates, floors and caps.** Compounding needs retained
  capitalization history; in-arrears and averaged conventions, floors and caps need their own
  declared terms and rule version.
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
| Browser | `src/Meridian.Ui/dashboard/src/screens/accounting-screen.entity-interest-accrual.tsx` | "Accrue interest" on an approved `LoanInterest` accounting profile |
| Desktop | `src/Meridian.Ui.Services/Services/Accounting/IntercompanyInterestAccrualWorkspace.cs`, `src/Meridian.Wpf/Views/IntercompanyInterestAccrualControl.xaml` | Interest accrual tab over the same two routes |
| Tests | `tests/Meridian.Tests/Storage/IntercompanyAccrualPostAndDeliverPostgresTests.cs` | PostgreSQL post at source, deliver, post at destination |
| Fixing contracts | `src/Meridian.Contracts/FundStructure/IntercompanyRateFixingDtos.cs` | Reviewed fixing record, requests and store interface (v2) |
| Fixing authority | `src/Meridian.Application/FundStructure/IntercompanyRateFixingRules.cs`, `IntercompanyRateFixingService.cs` | Maker-checker transitions and approved-value reads |
| Fixing stores | `src/Meridian.Storage/FundStructure/PostgresIntercompanyRateFixingStore.cs`, `FileIntercompanyRateFixingStore.cs` | Append-only versions; fund-structure migration `009` |
| Fixing routes | `src/Meridian.Ui.Shared/Endpoints/IntercompanyRateFixingEndpoints.cs` | `/api/ledger/intercompany/rate-fixings` list, propose and decide |
| Tests | `tests/Meridian.FundStructure.Tests/IntercompanyFloatingAccrualPreviewTests.cs`, `IntercompanyRateFixingServiceTests.cs` | v2 calculation, refusals and maker-checker |
| Tests | `tests/Meridian.Tests/Ui/IntercompanyAccrualDraftServiceTests.Floating.cs`, `IntercompanyRateFixingEndpointTests.cs`, `tests/Meridian.Tests/Storage/FundStructure/IntercompanyRateFixingStoreTests.cs` | Draft evidence, staleness, routes and durable stores |
| Desktop | `src/Meridian.Ui.Services/Services/Accounting/IntercompanyRateFixingWorkspace.cs`, `src/Meridian.Wpf/Views/IntercompanyRateFixingsControl.xaml` | Rate fixings tab |

The calculator, template, source draft, evidence guard, endpoints and the tests above are
implemented. Since 2026-11-16 both workstations can request an accrual over the same preview and
draft routes: the browser from an approved `LoanInterest` accounting profile, and WPF from an
Interest accrual tab. Each sends the operator's selection only, shows the server's computation, and
drafts with the evidence digest of the preview it showed. A PostgreSQL-backed run posts the source
accrual, delivers the counterpart proposal and posts the destination side; the legal registry,
profile history, entity lifecycle and journal drafts in that run are synthetic stand-ins. The WPF
control is compiled but its native interaction is proven only on the Windows runner. This decision
does not certify a release or move a roadmap row to done.

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
- A counterpart posted from a proposal counts as an accrual of its window in the destination book,
  so the other entity cannot accrue the same days again from its side.
- Before an accrual draft is submitted, approved or posted, posted principal is read again and must
  match what the draft retained. A backdated repayment or correction makes the draft stale until
  it is recomputed.
- The same recheck re-resolves the profile and legal revision retained for every slice, so a
  revision retired or re-pinned after drafting stops the draft, and it applies the overlap check to
  an incoming counterpart draft, so two entities that each accrued overlapping days cannot both
  post the other's counterpart.
- A reversal or rebook of a posted accrual gets its own posting identity.
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
